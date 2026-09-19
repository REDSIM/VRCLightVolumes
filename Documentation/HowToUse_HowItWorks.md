[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [Scripting API](./ScriptingAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# How VRC Light Volumes Work

| Menu |
| --- |
| [Overview](./HowToUse.md) |
| [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) |
| [Point Light Volumes](./HowToUse_PointLightVolumes.md) |
| [Froxel Clustering](./HowToUse_FroxelClustering.md) |
| [Shadows](./HowToUse_Shadows.md) |
| [Material Sources](./HowToUse_PointLightMaterialSources.md) |
| [AudioLink](./HowToUse_AudioLinkIntegration.md) |
| [TV Screens (Older Workflow)](./HowToUse_TVScreensIntegration.md) |
| [Debugging](./HowToUse_Debugging.md) |
| **How It Works**<br />• [Why A Grid Helps](#why-a-grid-helps)<br />• [Remembering Color And Direction](#remembering-color-and-direction)<br />• [Overlapping Volumes](#overlapping-volumes)<br />• [Point, Spot And Area Lights](#point-spot-and-area-lights)<br />• [Why Overlap Costs Performance](#why-overlap-costs-performance)<br />• [For Developers](#for-developers) |

A **Regular Light Volume** stores baked lighting across a room. A **Point Light Volume** calculates light from a Point, Spot or Area source as the scene renders. A shader that supports VRC Light Volumes uses both to light a surface.

The **Light Volume Manager** shares lighting with world and avatar shaders. To receive it, an avatar only needs a shader that supports VRC Light Volumes.

## Why A Grid Helps

Unity Light Probes store baked lighting at scattered points. An ordinary probe-lit Renderer uses one interpolated result for the whole object. A Regular Light Volume stores lighting on a 3D grid, with cells called **voxels**. The shader samples nearby voxels at each pixel, so one side of a character can catch the light from a doorway while the other stays dark.

VRC Light Volumes complements Unity Light Probes. Keep both: volumes provide local lighting detail, while ordinary probes support shaders without Light Volumes integration and provide fallback lighting.

![A Light Volume's grid of lighting samples inside a room](./Preview_3.png)

More voxels capture smaller lighting changes and take longer to bake. Start with a small, dense volume where you need detail. See [placement and resolution](./HowToUse_RegularLightVolumes.md).

Moving a baked volume moves the stored lighting with it. Re-bake when you want changes to the room, its lights or other objects to affect that lighting.

## Remembering Color And Direction

Each voxel stores **spherical harmonics (SH)**, which approximate light from different directions. Light Volumes uses first-order SH: **L0 + L1**.

- **L0** is the average light color. It looks the same from every surface direction.
- **L1** records how that color changes with direction. It lets a surface facing the light look brighter than a surface facing away.

![Lighting represented as an ambient color and directional components](./SH_01.png)

The same baked data in two [debug views](./HowToUse_Debugging.md):

| L0: average color | L0 + L1: color and direction |
| --- | --- |
| ![L0 debug view: spheres keep their local color but look flat without directional shading](./Images/debug-sh-l0.png) | ![L1 debug view: directional lighting gives the same spheres rounded shading](./Images/debug-sh-l1.png) |

Look at the spheres: L0 keeps their local lighting color, while L1 adds the change in brightness around their surfaces. The black walls are outside this volume.

Keep lightmaps for detailed static surfaces and reflection probes for room reflections. SH approximates lighting, so it can't preserve every sharp shadow. It can provide a simple highlight, but not a reflection of the room.

## Overlapping Volumes

Where volumes overlap, the Regular Light Volume with the highest **Weight** takes priority. Near its edge, **Smooth Blending** blends toward the next containing volume. Use this to blend a detailed room volume into a larger background volume.

Outside the Regular Light Volumes, **Light Probes Blending** uses Unity Light Probes. Turn it off to use the lowest-weight Regular Light Volume instead. **Sharp Bounds** stops edge blending where no other volume contains the surface; overlapping volumes can still blend.

An **Additive Light Volume** adds baked lighting on top of the base lighting. Use one for a lamp whose baked contribution switches on and off. Regular and Additive volumes share a limit of 32 active volumes.

## Point, Spot And Area Lights

Point Light Volumes calculate lighting without a voxel grid. They use the surface position and each light's shape, size, color and intensity. You can move them without re-baking the room.

![Area, Point and Spot Light Volumes lighting nearby surfaces](./Preview_4.png)

Point and Spot lights in **Parametric** mode fade with distance. A **cookie** gives the light a pattern: a cubemap for Point lights, or a 2D image for Spot and Area lights. An Area light emits from one side of a rectangle. Its image stays more detailed close to the rectangle and blends toward the average color farther away.

Moving a light doesn't update its shadows automatically. Bake them in the Editor for fixed surroundings, use **Bake In Game** for a startup capture, or add a [runtime shadow baker](./HowToUse_Shadows.md) for moving blockers.

## Why Overlap Costs Performance

Lights take more work when they overlap. Ten small lights in separate rooms can be cheaper than ten lights reaching the same wall.

**Froxel Clustering** divides the camera's view into 3D cells called froxels. Each cell lists the lights that might reach it, so surfaces can skip the others. The shader still checks the light and shadow at each pixel. Outside the grid, it checks lights through the ordinary light loop.

**Additive Max Overdraw** limits the Additive Light Volumes and Point Light Volumes per pixel. Each group has its own counter with that limit. Lower values can leave out visible lights. Clustering doesn't raise these limits or the active-light limits.

Use [Best Practices](./BestPractices.md) for practical tuning and the [clustering guide](./HowToUse_FroxelClustering.md) for its settings.

## For Developers

To add lighting to your shader, start with [Shader Integration](./ForDevelopers.md). Use the [UdonSharp API](./ScriptingAPI.md#udonsharp-api) to change lights in-game, or the [Unity Editor API](./UnityEditorAPI.md) to bake and process their data.

For texture formats and other implementation details, see the [technical reference](./TechnicalDetails.md#baked-volume-data).
