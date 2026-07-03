[VRC Light Volumes](../README.md) | [How to Use](../Documentation/HowToUse.md) | **Best Practices** | [Udon Sharp API](../Documentation/UdonSharpAPI.md) | [For Shader Developers](../Documentation/ForShaderDevelopers.md) | [Compatible Shaders](../Documentation/CompatibleShaders.md)

# Best Practices

| Menu |
| ----|
| **Best Practices**<br />- [Regular Light Volumes Use Cases](#Regular-Light-Volumes-Use-Cases)<br />- [Point Light Volumes Use Cases](#Point-Light-Volumes-Use-Cases)<br />- [Tuning Point Light Volume Size](#Tuning-Point-Light-Volume-Size)<br />- [Area Light Cookie Emission](#Area-Light-Cookie-Emission)<br />- [Additive Volumes](#Additive-Volumes)<br />- [Point Light Volume Baked Realtime Shadows](#Point-Light-Volume-Baked-Realtime-Shadows)<br />- [Quest And Mobile Shadow Artifacts](#Quest-And-Mobile-Shadow-Artifacts)<br />- [Point Light Volume Realtime Shadows](#Point-Light-Volume-Realtime-Shadows)<br />- [Custom Render Textures Projections](#Custom-Render-Textures-Projections)<br />- [Naming Light Volumes](#Naming-Light-Volumes)<br />- [Volume Bounds Smoothing](#Volume-Bounds-Smoothing)<br />- [Culling Light Volumes](#Culling-Light-Volumes)<br />- [Moving Light Volumes](#Moving-Light-Volumes)<br />- [Spawning New Light Volumes In Runtime](#Spawning-New-Light-Volumes-In-Runtime)<br />- [Bakery Volume Rotation](#Bakery-Volume-Rotation)<br />- [Fixing Bakery Light Probes](#Fixing-Bakery-Light-Probes)<br />- [Shader Path Choices](#Shader-Path-Choices) |

## Regular Light Volumes Use Cases

- Use them with small static props that usually require very high lightmap resolution to avoid visible seams. Light Volumes produce no seams because they are voxel-based.
- Dynamic batching support: if you have many low-poly dynamic props using the same material, and their Mesh Renderers have Light Probes and Reflection Probes disabled, they can be dynamically batched at runtime.
- Combine Light Volumes with particles to create volumetric fog effects.
- Switch between two Light Volumes at runtime to create toggleable lighting for rooms or other areas in your scene.
- TV screens dynamic Global Illumination.
- Audio Link dynamic lights.

## Point Light Volumes Use Cases

- Spot Lights as portable flashlights.
- Point Lights as other dynamic light sources.
- Area Lights as studio light soft boxes.
- Area Lights with Cookie as textured emissive panels, TV screens, windows and signs.
- Moving blinking lighting for clubs.
- Image and cubemap projectors.
- TV screens dynamic Global Illumination.
- Audio Link dynamic lights.

## Tuning Point Light Volume Size

Tune Point Light Volume source size intentionally. For Point and Spot Lights, `Light Source Size` affects calculated range and size-aware specular highlights. For Area Lights, `Width` and `Height` are the visible source size.

For glossy PBR materials in modern compatible shaders, larger Point, Spot and Area Light sources create broader and softer specular highlights, while smaller sources create sharper highlights. Do not use `Light Source Size` only as a range control when the light is visible in reflections. Set the physical source size first, then adjust `Intensity`, `Brightness Cutoff` and culling behavior.

## Area Light Cookie Emission

Use **Area Light Cookie Emission** when a rectangular source needs to emit non-uniform color in runtime: TV screens, monitors, windows with colored patterns, animated signs, LED walls or soft boxes with texture detail.

For new screen-light setups, prefer Area Light Cookie Emission over the old **LightVolumeTVGI** script. TVGI only drives lighting from a single average screen color and is mostly a legacy workflow now. Area Light Cookie Emission projects the actual texture near the screen and gradually blends toward the average color with distance.

For runtime screens, keep `Cookie Resolution` as low as acceptable and enable `Auto Update Textures` only for RenderTexture or Material sources that actually change. See [Area Light Emission](../Documentation/HowToUse_AreaLightEmission.md) for the full setup workflow.

## Additive Volumes

For dynamic lighting, set the volume to **Additive** so it layers on top of others and also affects lightmapped static meshes with a compatible shader. Minimize overlapping additive volumes to reduce overhead.

Lower `Additive Max Overdraw` when overlap-heavy areas become expensive. It caps additive Light Volume accumulation, Point Light Volume diffuse contribution and individual Point Light Volume specular evaluation. Lower values improve worst-case performance, but can hide lower-priority overlapping lights.

## Point Light Volume Baked Realtime Shadows

Point Light Volume Shadows are shadows similar to Unity's realtime point light shadows, but they are intended to work mostly in baked mode, which is much more optimized. You usually prebake depth shadow maps in the editor, or bake them only once on start in runtime, and use this data to project shadows even on movable dynamic objects. Dynamic objects will not cast shadows themselves in that mode. This is usually enough for most cases and gives much more performant behavior than a regular realtime approach.

Point Light Volume Shadows use **Exponential Variance Shadow Maps (EVSM)**. They are cheap to filter, support wide blur kernels well, and keep the same shadow pipeline on PC and Quest.

Editor-baked shadow blur uses the spherical shadow-space blur path, so larger `Blur` values stay more consistent across cubemap faces and single-slice Spot Light projections.

Use shadows only where they visibly matter. Shadowed Point Light Volumes need extra texture memory and extra shader work, so they are heavier, especially for Quest and Mobile.

Spot Lights with shadows are usually the most memory-efficient shadowed Point Light Volume type. With `Force Cubemap Shadows` disabled and an angle below 180 degrees, a Spot Light uses one projected shadow texture instead of six cubemap faces. Point Lights, Area Lights and Spot Lights with `Force Cubemap Shadows` enabled use cubemap shadows, which cost six texture slices.

For Spot Lights with shadows, keep the angle around 120 degrees or lower when possible. As the angle approaches 180 degrees, the single projected shadow texture has worse effective resolution. A 180 degree Spot Light is not efficient, and angles above 180 degrees automatically use cubemap shadows, which are about six times more expensive in shadow texture memory.

Keep `Shadow Resolution` as low as acceptable. Shadow precision is selected automatically from the active build target: Android/Quest/iOS uses `Half`, while PC uses `Float`. Increase `Bias` only enough to hide self-shadow artifacts, because large bias detaches contact shadows.

See [Point Light Volume Shadows](../Documentation/HowToUse_Shadows.md) for the full setup workflow.

## Quest And Mobile Shadow Artifacts

Mobile and Quest EVSM shadows can show noisy or glitchy bright artifacts on shadow edges, in mesh corners, and near the first contact area where the shadow starts next to the occluder. This mostly comes from mobile precision limits; Android/Quest/iOS uses `Half` precision shadow textures under the hood, while PC uses `Float`.

**Light Volume Setup** stores `Shadow Min Variance` separately for PC and Android/Quest/iOS and shows only the value for the active Unity build target. Tune the mobile value while the project is switched to Android or iOS. Tune the PC value while the project is switched to a desktop target.

For Quest and Mobile shadow edge noise, corner glitches or contact-start artifacts, start from `Shadow Min Variance = 1` on the Android/Quest/iOS setting. This is the default mobile-oriented value and often the correct value for Half precision. PC usually works best with `Shadow Min Variance = 0` or another very low value for cleaner contact shadows.

Then increase `Shadow Bleed Reduction` if bright speckles or small halo artifacts remain; `0.2..0.4` is usually a reasonable Quest range. Do not fix these artifacts mostly with `Bias`. Bias is for self-shadow acne, and large values quickly detach contact shadows. If stronger variance or bleed reduction makes the shadow too thin, add a little more per-light `Blur` instead.

On mobile, it may be impossible to remove every artifact completely, but good `Shadow Min Variance`, `Shadow Bleed Reduction`, `Bias`, `Blur`, `Near Plane` and `Far Clip Plane` tuning can reduce them heavily.

See [Point Light Volume Shadows](../Documentation/HowToUse_Shadows.md) for the full setup workflow.

## Point Light Volume Realtime Shadows

Realtime shadow baking is available through the extra **Point Light Shadow Runtime Baker** Udon script with `Realtime` mode enabled there.

Point Light Volume Realtime Shadows are **not cheaper than Unity's realtime shadows**. They are heavier. Under the hood, **Point Light Shadow Runtime Baker** renders cameras and blur passes in runtime, which means 2 draw calls for a single realtime Spot Light and 7 draw calls for a single Point Light or Area Light. Use it for a small number of important lights and choose culling layers carefully.

Keep `Spherical Blur` disabled for realtime shadows unless the cheaper `Planar Blur` produces visible cubemap seams or Spot Light projection-edge artifacts. Spherical blur reduces those artifacts but adds more expensive shadow-space samples.

It is not recommended to use **Point Light Shadow Runtime Baker** in realtime mode for Quest and Mobile. It is much heavier than Unity's default realtime shadows, especially on CPU side.

See [Point Light Volume Shadows](../Documentation/HowToUse_Shadows.md) for the full setup workflow.

## Custom Render Textures Projections

In Point Light Volume `Custom` projection mode, you can use regular **Textures**, **Render Textures**, **Custom Render Textures** and **Materials** to render dynamic animated cookies and cubemaps. They will be auto-updated every frame if `Auto Update Textures` is enabled in **Light Volume Manager**. Use the smallest **Cookie Resolution** that still gives acceptable quality. Larger resolutions are more expensive with dynamic animated sources such as **Render Textures**, **Custom Render Textures** and **Materials**.

Prefer using **Material** instead of **Custom Render Texture** when possible. It is cheaper because it needs fewer **Blit()** operations.

Cookie source Materials are rendered as texture generators, not as normal world materials. A simple Unlit material is usually enough for cookies. Non-Unlit materials can add lighting and shading into the cookie texture itself, which is usually not what you want. See [Point Light Material Sources](../Documentation/HowToUse_PointLightMaterialSources.md) for compatible Material setup rules.

With `Auto Update Textures` disabled in **Light Volume Manager**, projection sources are copied when the custom texture array is initialized or rebuilt. Use `ReinitializeCustomTextures()` after advanced manual source replacement, or keep `autoUpdate = true` and use `UpdateAutoCustomTextures()` when you intentionally manage the update call yourself.

For Point Light Volume Shadows, runtime bakers can update one managed shadow slice directly with `UpdatePointLightShadowTextureSlice()`.

Runtime projection sources are shared by source object and auto-update mode. Several lights using the same Texture, RenderTexture, Cubemap or Material with the same `autoUpdate` value share one runtime texture-array entry. The same source used once with `autoUpdate = false` and once with `autoUpdate = true` gets separate entries, so an auto-updated copy cannot overwrite a static captured copy.

## Naming Light Volumes

Ensure every Light Volume you bake has a unique game object name. The generated 3D textures inherit these names and can conflict. If you duplicate baked volumes or use prefab instances with the `Bake` flag disabled, you do not need to rename them.

Also give shadowed Point Light Volumes unique game object names before baking shadows. Baked shadow assets use the Point Light Volume game object name too, so two lights with the same name can overwrite each other's shadow files.

## Volume Bounds Smoothing

Overlap intersecting volumes slightly, about 0.25 m, to hide seams. The `Smooth Blending` parameter controls edge falloff, so keep it smaller than your overlap.

To smooth between a volume and uncovered areas, disable `Sharp Bounds` in Light Volume Setup. This applies smoothing to all edges, so you might need to scale up your volumes to keep the softened edges outside of the intended area.

## Culling Light Volumes

At runtime, you can disable any Light Volume to exclude it from rendering. This works even on non-dynamic volumes. Manually culling unused volumes can significantly boost performance in large scenes.

Disabling **Light Volumes Manager** object disables the whole Light Volumes system and makes shaders fall back to light probes.

## Moving Light Volumes

To update a volume's transform in runtime, enable **Dynamic** on its component and enable **Auto Update Volumes** in Light Volume Setup. Otherwise, you must manually update positions of Dynamic volumes from an Udon script. Color, Intensity, enabling and disabling update without **Auto Update Volumes**. If you do not need runtime transform updates, leave both options off for better performance.

When changing Light Volume or Point Light Volume data from Udon, prefer the instance setter methods such as `SetColor()`, `SetIntensity()`, `SetDynamic()`, `SetAdditive()`, `SetLightSourceSize()` and `SetShadowSettings()` instead of writing public fields directly. These methods skip unchanged values and notify the manager with a more targeted update.

## Spawning New Light Volumes In Runtime

You can spawn and auto-register both Light Volumes and Point Light Volumes in runtime.

The usual VRChat/Udon path is to make the light part of a Player Object. In that setup, each player's object creates its own Light Volume instance and it registers with the scene **Light Volume Manager** automatically when the object becomes active.

If you use a prefab workflow instead, first set up the light in the editor and configure it as needed. Then remove the C# Unity helper component, `Light Volume` or `Point Light Volume`, from the prefab and leave only the **Udon Sharp Instance** component there. The helper component is for authoring and editor sync; the Udon instance is the runtime component that registers with the manager.

Make sure the instance has a reference to the scene **Light Volume Manager**. On `Start()` or `OnEnable()`, the instance registers itself with the manager automatically. If you instantiate a prefab from Udon, that prefab may not have a valid manager reference yet. In that case, set `LightVolumeManager` from another script after spawning; the instance registers itself when that variable changes.

If you create or modify instances manually from another Udon script, you can use `InitializeLightVolume()` / `InitializePointLightVolume()` and `DeinitializeLightVolume()` / `DeinitializePointLightVolume()` in the **Light Volume Manager** when needed.

When a spawned Point Light Volume with a cookie, cubemap, LUT, Material, RenderTexture or shadow source is registered through the manager, the manager marks the shared texture arrays dirty and rebuilds them automatically before the updated shader data is uploaded. Use `ReinitializeCustomTextures()` or `ReinitializeShadowTextures()` only for advanced manual workflows where you replace texture sources directly without going through the normal registration or setter path.

When assigning projection sources at runtime with `SetCustomTexture()` or `SetCustomMaterial()`, pass `autoUpdate = true` only for sources that need per-frame refresh and keep **Auto Update Textures** enabled on the manager. Static sources should use `autoUpdate = false`. If you mix both modes on the same source object, the manager intentionally stores them in separate runtime slices.

## Bakery Volume Rotation

Bakery lightmapper offers high quality with Light Volumes but may not support rotation during baking in some older versions. Upgrade to the latest Bakery Patch through **Bakery > Utilities > Check for Patches** to support full rotation. Runtime rotation is still always supported.

## Fixing Bakery Light Probes

Bakery bakes L1 probes to work with "Geometrics SH Evaluation", which can cause overexposure and underexposure issues. Enable **Fix Light Probes L1** in Light Volume Setup to correct the probes after each bake. This may reduce overall contrast slightly but prevents over or underexposure.

## Shader Path Choices

Use `LightVolumeSHSpecular()` or the ASE **Light Volume SH Specular** node for the most correct glossy PBR surface shaders where individual Point Light Volume highlights are visible. This path gives Point Light Volumes their own source-size aware speculars, shadows, cookies and per-surface shading, but it is more expensive than regular `LightVolumeSH()` when several Point Light Volumes overlap.

Use `LightVolumeSH()` for regular avatar shaders, toon shaders and surfaces where individual Point Light Volume speculars are not needed. It is also the better default when the material has no specular response at all.

Use `LightVolumeSH_L0()` when directionality is not important, for example particles or sometimes plant foliage. It is cheaper because it only returns the L0 ambient term.

If you only need a cheap glossy response from already accumulated SH data, use `LightVolumeSpecularDominant()` or `LightVolumeSpecular()` instead of the full `LightVolumeSHSpecular()` path.

Choose the shader stage intentionally. If the material has heavy overdraw, such as particles or foliage, calculate Light Volumes in the vertex stage when the quality tradeoff is acceptable. For normal surface shaders and avatar shaders, calculate Light Volumes in the fragment stage for better local detail and fewer interpolation artifacts.
