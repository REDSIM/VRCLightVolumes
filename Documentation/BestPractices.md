[VRC Light Volumes](../README.md) | [How to Use](../Documentation/HowToUse.md) | **Best Practices** | [Udon Sharp API](../Documentation/UdonSharpAPI.md) | [For Shader Developers](../Documentation/ForShaderDevelopers.md) | [Compatible Shaders](../Documentation/CompatibleShaders.md)

# Best Practices

| Menu |
| ----|
| **Best Practices**<br />• [Regular Light Volumes Use Cases](#Regular-Light-Volumes-Use-Cases)<br />• [Point Light Volumes Use Cases](#Point-Light-Volumes-Use-Cases)<br />• [Area Light Emission](#Area-Light-Emission)<br />• [Point Light Volume Baked Realtime Shadows](#Point-Light-Volume-Baked-Realtime-Shadows)<br />• [Point Light Volume Realtime Shadows](#Point-Light-Volume-Realtime-Shadows)<br />• [Custom Render Textures Projections](#Custom-Render-Textures-Projections)<br />• [Naming Light Volumes](#Naming-Light-Volumes)<br />• [Volume Bounds Smoothing](#Volume-Bounds-Smoothing)<br />• [Culling Light Volumes](#Culling-Light-Volumes)<br />• [Moving Light Volumes](#Moving-Light-Volumes)<br />• [Additive Volumes](#Additive-Volumes)<br />• [Bakery Volume Rotation](#Bakery-Volume-Rotation)<br />• [Fixing Bakery Light Probes](#Fixing-Bakery-Light-Probes)<br />• [Spawning New Light Volumes In Runtime](#Spawning-New-Light-Volumes-In-Runtime) |

## Regular Light Volumes Use Cases

- Use them with small static props that usually require very high lightmap resolution to avoid visible seams. Light Volumes produce no seams at all, as they are voxel-based.
- Dynamic batching support: if you have tons of low-poly dynamic props across your scene using the same material, and their Mesh Renderers have Light Probes and Reflection Probes disabled, they can be dynamically batched at runtime, potentially improving performance.
- Combine Light Volumes with particles to create stunning volumetric fog effects.
- Switch between two Light Volumes at runtime to create toggleable lighting for rooms or other areas in your scene.
- TV Screens dynamic Global Illumination
- Audio Link Dynamic Lights
- And much more!

## Point Light Volumes Use Cases

- Spot Lights as portable flashlights
- Point Lights as other dynamic light sources
- Area Lights as studio light soft boxes
- Area Lights with Cookie as textured emissive panels, TV screens, windows and signs
- Moving blinking lighting for clubs
- Image and cubemaps projectors
- TV Screens dynamic Global Illumination
- Audio Link Dynamic Lights
- And much more!

## Area Light Emission

Use **Area Light Emission** when a rectangular source needs to emit non-uniform color in runtime: TV screens, monitors, windows with colored patterns, animated signs, LED walls or soft boxes with texture detail.

For new screen-light setups, prefer Area Light Emission over the old **LightVolumeTVGI** script. TVGI only drives lighting from a single average screen color and is mostly a legacy workflow now. Area Light Emission projects the actual texture near the screen and gradually blends toward the average color with distance.

Keep using baked regular or additive Light Volumes when the light is fully static and does not need runtime texture changes. Baking is still cheaper for large static illumination zones.

For runtime screens, keep `Cookie Resolution` as low as acceptable and enable `Auto Update Textures` only for RenderTexture or Material sources that actually change.

## Point Light Volume Baked Realtime Shadows

Point Light Volume Shadows are shadows similar to Unity's realtime point light shadows, but they are made to work mostly in baked mode, which is much more optimized! You usually prebake depth shadow maps in editor (or only once on start in runtime) and use this data to project shadows even on movable dynamic objects. However, dynamic objects will not cast shadows themselves in that case. It's usually more than enough in most of the cases, and it gives a much more persormant behaviour than regular realtime approach.

Point Light Volume Shadows use **Exponential Variance Shadow Maps (EVSM)**. They are cheap to filter, support wide blur kernels well, and keep the same shadow pipeline on PC and Quest.

Editor-baked shadow blur uses the spherical shadow-space blur path, so larger `Blur` values stay more consistent across cubemap faces and single-slice Spot Light projections.

Use shadows only where they visibly matter. Shadowed Point Light Volumes need extra texture memory and extra shader work, so they are heavier, especially for Quest and Mobile!

**Spot lights** with shadows (when `Force Cubemap Shadows` disabled) uses one single shadow texture, which is 6 times more memory efficient than **Point** or **Area** lights.
**Point lights**, **Area lights** and **Spot lights** with `Force Cubemap Shadows` enabled uses shadow cubemap textures, which bakes shadows all around the light source, in all directions from it, which uses 6 different shadow taxtures under the hood.

For Spot Lights below 180 degrees, prefer the default single projected shadow texture. Enable `Force Cubemap Shadows` only when the projection really needs cubemap behavior. For example, when you need to animate the rotation of a **Spot Light** and still need shadows in all directions.

Keep `Shadow Resolution` as low as acceptable. Shadow precision is selected automatically from the active build target: Android/Quest/iOS uses `Half`, while PC uses `Float`. Increase `Bias` only enough to hide self-shadow artifacts, because large bias detaches contact shadows.

If `Half` shadows show noisy bright rims or light leaking on Quest, tune the global EVSM controls in **Light Volume Setup** instead of relying on `Bias`: raise `Shadow Bleed Reduction` first, then adjust `Shadow Min Variance` if needed, and compensate lost penumbra with a little more per-light `Blur`.

See [Point Light Volume Shadows](../Documentation/HowToUse_Shadows.md) for the full setup workflow. 

## Point Light Volume Realtime Shadows

Realtime shadow baking is available through the extra **Point Light Shadow Runtime Baker** udon script with `Realtime` mode enabled there. 

Point Light Volume Realtime Shadows are **NOT cheaper than Unity's realtime shadows**, they are heavier! Under the hood **Point Light Shadow Runtime Baker** renders cameras and blur passes in runtime, which means x2 draw calls for a single realtime **Spot light** and x7 draw calls for a single **Point light** or **Area Light**.
So use it for a small number of important "heroic" lights and choose culling layers carefully! 

Keep `Spherical Blur` disabled for realtime shadows unless the cheaper `Planar Blur` produces visible cubemap seams or Spot Light projection-edge artifacts. Spherical blur reduces those artifacts but adds more expensive shadow-space samples.

It is not recommended using **Point Light Shadow Runtime Baker** in realtime mode for Quest and Mobile, it is much heavier than Unity's default realtime shadows, especially on CPU side.

See [Point Light Volume Shadows](../Documentation/HowToUse_Shadows.md) for the full setup workflow. 

## Custom Render Textures Projections

In Point Light Volume `Custom` projection mode, you can use regular **Textures**, **Render Textures**, **Custom Render Textures** and **Materials** to render dynamic animated cookies and cubemaps. They will be auto updated every frame if you have `Auto Update Textures` enabled in **Light Volume Manager**. Set up as small **Cookie Resolution** in **Light Volume Manager** as you can afford with your desired with graphics quality. Larger resolution is more expensive with dynamic animated textures such as **Render Textures**, **Custom Render Textures** and **Materials**.

Prefer using **Material** instead of a **Custom Render Textuere** - it's cheaper because uses less **Blit()** operations. 

With `Auto Update Textures` disabled in **Light Volume Manager**, you can triger a **Light Volume Manager** method `UpdatePointLightShadowTextureSlice()` to manually updated a desired slice of a desired Point Light's dynamic texture.

Completely similar approach works with Point Light Volume Shadows as well.

## Naming Light Volumes

Ensure every Light Volume you bake has a unique game object name. The generated 3D textures inherit these names and can conflict. If you duplicate baked volumes or use prefab instances with the `Bake` flag disabled, you don’t need to rename them.

## Volume Bounds Smoothing

Overlap intersecting volumes slightly (about 0.25 m) to hide seams. The `Smooth Blending` parameter controls edge falloff - keep it smaller than your overlap.

To smooth between a volume and uncovered areas, disable `Sharp Bounds` in Light Volume Setup. This applies smoothing to all edges, so you might need to scale up your volumes to keep the softened edges outside of the intended area.

## Culling Light Volumes

At runtime, you can disable any Light Volume to exclude it from rendering. This works even on non-dynamic volumes. Manually culling unused volumes can significantly boost performance in large scenes.

Disabling **Light Volumes Manager** object disables all the Light Volumes system and fallbacks all the shaders to light probes.

## Moving Light Volumes

To update a volume's transform in runtime, enable **Dynamic** on it's component and check **Auto Update Volumes** in Light Volume Setup. Otherwise, you must manually update positions of Dynamic volumes via an Udon script. Color, Intensity, enabling and disabling update without **Auto Update Volumes**. If you don’t need runtime transform updates, leave both options off for better performance.

When changing Light Volume or Point Light Volume data from Udon, prefer the instance setter methods such as `SetColor()`, `SetIntensity()`, `SetDynamic()`, `SetAdditive()`, `SetLightSourceSize()` and `SetShadowSettings()` instead of writing public fields directly. These methods skip unchanged values and notify the manager with a more targeted update.

## Additive Volumes

For dynamic lighting, set the volume to **Additive** so it layers on top of others and also affects lightmapped static meshes with a compatible shader. Minimize overlapping additive volumes to reduce overhead. Use **Additive Max Overdraw** value in Light Volume Setup to limit how many additive volumes and Point Light Volumes can affect a pixel. Lower values improve worst-case performance in overlap-heavy areas.

## Bakery Volume Rotation

Bakery lightmapper offers high quality with Light Volumes but may not support rotation during baking in some older versions. Upgrade to the latest Bakery Patch (via **Bakery → Utilities → Check for Patches**) to support full rotation. Runtime rotation is still always supported.

## Fixing Bakery Light Probes

Bakery bakes L1 probes to work with "Geometrics SH Evaluation", which can cause overexposure and underexposure issues. Enable **Fix Light Probes L1** in Light Volume Setup to correct the probes after each bake. This may reduce overall contrast slightly but prevents over or underexposure.

## Spawning New Light Volumes In Runtime

You can spawn and auto-register both Light Volumes and Point Light Volumes in runtime. To do that, first, setup your light volume in editor and configure it as you wish. Remove the `Light Volume` or the `Point Light Volume` authoring component from the prefab, leaving only the **Udon Sharp Instance script** there.

Make sure the instance keeps a reference to the scene **Light Volume Manager**. On `Start()` or `OnEnable()`, the instance registers itself with the manager automatically. If you create or modify instances manually from another Udon script, you can use `InitializeLightVolume()` / `InitializePointLightVolume()` and `DeinitializeLightVolume()` / `DeinitializePointLightVolume()` in the **Light Volume Manager** when needed.

If a spawned Point Light Volume uses a new cookie, cubemap, LUT, Material, RenderTexture, or shadow source, the manager may need to rebuild the shared texture arrays. You can use `ReinitializeCustomTextures()` or `ReinitializeShadowTextures()` to reinitialize it manually.
