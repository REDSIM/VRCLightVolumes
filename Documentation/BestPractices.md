[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | **Best Practices** | [Scripting API](./ScriptingAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Best Practices

| Menu |
| --- |
| **Best Practices**<br />• [Support Avatars With Light Probes And Light Volumes](#support-avatars-with-light-probes-and-light-volumes)<br />• [Choose The Lighting Type For The Job](#choose-the-lighting-type-for-the-job)<br />• [Place Volumes For Your World](#place-volumes-for-your-world)<br />• [Name Baked Volumes Uniquely](#name-baked-volumes-uniquely)<br />• [Batch Small Moving Props](#batch-small-moving-props)<br />• [Blend Between Volumes](#blend-between-volumes)<br />• [Keep Point Light Ranges Tight](#keep-point-light-ranges-tight)<br />• [Use Clustering For Many Local Lights](#use-clustering-for-many-local-lights)<br />• [Limit Excessive Overlap](#limit-excessive-overlap)<br />• [Choose How Shadows Update](#choose-how-shadows-update)<br />• [Check Mobile Shadow Quality Separately](#check-mobile-shadow-quality-separately)<br />• [Keep Animated Sources Affordable](#keep-animated-sources-affordable)<br />• [Update Only What Changes](#update-only-what-changes)<br />• [Preserve Features Used By Scripts](#preserve-features-used-by-scripts)<br />• [Spawn Lights From Prefabs](#spawn-lights-from-prefabs)<br />• [Bakery Tips](#bakery-tips)<br />• [Match Avatar Brightness To The World](#match-avatar-brightness-to-the-world)<br />• [Check Before Release](#check-before-release)<br />• [Migrating from 2.x.x to 3.x.x](#migrating-from-2xx-to-3xx) |

## Support Avatars With Light Probes And Light Volumes

Keep both **Unity Light Probes** and **Light Volumes** in your world. Light Volumes light avatars whose shaders support them. Ordinary probes light avatars with other shaders. Probes also provide fallback lighting outside your volumes.

## Choose The Lighting Type For The Job

| Situation | A useful starting point |
| --- | --- |
| Room lighting, sunlight and many stationary lamps | Use your lightmapper's lights and bake them into **Regular Light Volumes**. Keep lightmaps for surfaces that need detailed baked shadows. |
| A group of lamps that switches as one | Bake an **Additive Light Volume** separately from the base lighting. |
| Two lighting states for the same room | Bake a **Regular Light Volume** for each state, then enable only the one you need. This does not switch the room's lightmaps. |
| Flashlight or projector | A **Spot Light Volume**. A narrow cone avoids lighting unrelated parts of the scene. |
| Portable bulb or independently animated lamp | A **Point Light Volume**. |
| Screen, sign or soft panel | An **Area Light Volume**. Add a texture when its image should affect the light. |
| Music-reactive club lighting | Control Point, Spot or Area lights with [AudioLink](./HowToUse_AudioLinkIntegration.md). |
| Small props with visible lightmap seams | Use a shader that supports VRC Light Volumes to light the prop from a Regular Light Volume instead of a lightmap. Check that the grid has enough detail for the prop. |
| Lit particles or fog meshes | Use a particle shader that supports VRC Light Volumes. Keep the number of overlapping transparent layers low. |

Regular Light Volumes store lighting baked from your lightmapper's lights. Use Point Light Volumes when you need to control each light separately in game.

Use an **Additive Light Volume** for a baked lighting layer that you need to move or change in game. It adds to the surrounding lighting and can also light lightmapped surfaces when their shader supports that combination.

Use a Point Light Volume's **Bake Into Probes** only for lights that should remain in the ordinary probe lighting. Switching that light off in game does not remove its already-baked contribution from Unity Light Probes.

## Place Volumes For Your World

In worlds with separate rooms, give each room its own volume. Overlap neighboring volumes at doorways and connecting passages.

For open worlds, use one large volume with very low resolution for broad lighting. Add smaller volumes with higher resolution where detailed lighting matters.

## Name Baked Volumes Uniquely

Give each volume you bake a unique GameObject name: its source textures use that name and can overwrite each other. Copies with **Bake** disabled can share the same baked textures.

## Batch Small Moving Props

For many small moving props that share a material, test [Unity's dynamic batching](https://docs.unity3d.com/2022.3/Documentation/Manual/dynamic-batching.html). Light Volumes can still light these props with **Light Probes** disabled on their Mesh Renderers. Compare frame time before keeping the change, because batching also takes CPU time.

## Blend Between Volumes

Keep the overlap between neighboring volumes at least as wide as their **Smooth Blending** region. For example, use at least `0.25 m` of overlap with a `0.25 m` blend region.

Set **Weight** in the Manager's **Light Volumes** list. Keep a broad fallback volume at a low weight and detailed room volumes at higher weights.

The Manager's three-dot menu contains **Sort Light Volumes**. It sorts equal-weight volumes by resolution settings: manual resolution first, then higher **Voxels Per Unit**. Set different weights when an overlap needs a specific priority.

For edges adjoining uncovered areas, enable **Light Probes Blending** and disable **Sharp Bounds**. Extend the volume beyond the area that needs its full lighting, because the outer edge now fades toward the ordinary probes.

See [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) for blending controls and Additive baking.

## Keep Point Light Ranges Tight

Enable **Debug Range** and look at the whole affected area, not just the bright patch beside the lamp.

For **Parametric** and **Custom** Point/Spot lights, effective range depends on **Light Source Size**, Transform scale, **Color**, **Intensity** and the Manager's **Brightness Cutoff**. **LUT** lights have an explicit **Range**.

Tune a light in this order:

1. Set **Light Source Size** to a sensible emitter radius.
2. Set color and intensity for the desired appearance.
3. Check **Debug Range** for unnecessary overlap.
4. Raise the Manager's **Brightness Cutoff** a little if imperceptibly dim light extends too far. This affects all lights that use the calculated cutoff.
5. Disable lights in zones that cannot contribute to the current view.

Light Source Size also changes the width of glossy highlights in shaders with individual-light specular support. Shrinking it and compensating with much higher intensity may change the look without solving the overlap.

## Use Clustering For Many Local Lights

Keep **Clustering Enabled** on in the **Light Volume Manager's Froxel Clustering** section. [Froxel Clustering](./HowToUse_FroxelClustering.md) builds a short light list for each part of the player's view. Each visible surface checks lights from that list instead of every active light in the scene.

The default settings usually work best. Change them only when you understand the grid settings and want to tune performance for a specific scene. Clustering helps most when many lights occupy separate areas. Heavy overlap still costs time.

Enable [Shadow Culling (Hi-Z)](./HowToUse_FroxelClustering.md#shadow-culling-hi-z) when walls, floors or ceilings put large parts of a light's range in shadow. This lets clustering skip that light in fully shadowed cells.

## Limit Excessive Overlap

**Additive Max Overdraw** on the Manager limits overlapping Additive volumes and Point Light Volumes separately.

For example, `4` allows up to four Additive volumes and up to four Point Light contributions per pixel.

Lowering it may improve frame rate, but lights can disappear where the limit is reached. Test the most crowded overlap and use the smallest value that keeps the lighting you need.

## Choose How Shadows Update

**Bake In Game** is usually best for shadows from static world geometry. It generates shadow maps after joining instead of including them in the world's asset bundle, reducing download size.

Keep shadows baked in the Editor when you cannot bake them in game or the runtime result does not meet your quality needs.

**Realtime** shadows are very expensive. Reserve them mainly for a few Spot-light flashlights that need soft moving shadows, such as in horror worlds. See [Shadows](./HowToUse_Shadows.md) for the available workflows.

## Check Mobile Shadow Quality Separately

Test shadows with the intended build target selected, then verify them on the device.

For mobile speckles or noisy shadow edges, start with the Manager's mobile **Shadow Min Variance** at `1` and try **Shadow Bleed Reduction** around `0.2–0.4`. Compare against the defaults in the same view: stronger settings can change thin shadows and contact edges.

Use per-light **Bias** for self-shadow artifacts and **Blur** for softness. These are bake settings, so rebake after changing them.

## Keep Animated Sources Affordable

Use the smallest acceptable **Cookie Resolution**. A Point light's cubemap needs six images. A Spot cookie or an Area emitter texture uses one.

Use a [custom Material shader](./HowToUse_PointLightMaterialSources.md) for animated patterns and special effects. Keep its calculations affordable at the chosen resolution.

Enable the Manager's **Auto Update Textures** for live Render Textures and Materials. For a scripted source that should freeze, use the projection setter with `autoUpdate = false`.

See [Material Sources](./HowToUse_PointLightMaterialSources.md) and [Area Light Cookies](./HowToUse_PointLightVolumes.md#area-light-cookies) for setup.

## Update Only What Changes

Enable **Dynamic** for volumes whose position, rotation or scale changes in game, and enable the Manager's **Auto Update Volumes** to follow those changes automatically. Leave **Dynamic** off for stationary objects. If nothing moves, you can disable **Auto Update Volumes** too.

Color and intensity changes do not need **Dynamic**. Use the [UdonSharp setters](./ScriptingAPI.md#udonsharp-api), such as `SetColor()` and `SetIntensity()`, when controlling lights from scripts.

When the player moves between large, separate zones, disable the previous zone's Light Volume and Point Light Volume GameObjects and enable the new zone's lights. This also works for volumes with **Dynamic** off.

For frequently blinking lights, animate intensity and keep it slightly above zero. Disabling a light or setting its intensity to zero makes the CPU rebuild the active light list. Use GameObject toggles for zone changes, where those updates happen less often.

> [!NOTE]
> A scene can contain more than **32 Regular/Additive volumes** and **128 Point/Spot/Area lights** if unused zones stay disabled. These limits apply to objects active at the same time. Enabled lights outside the camera's view still count.

Disabling the **Light Volume Manager** turns off Light Volumes and returns supporting shaders to Unity Light Probes.

## Preserve Features Used By Scripts

The Manager's **Shader Stripping** removes unused features from Play Mode and world builds. **Auto** detects features configured in the scene, including disabled lights.

It cannot predict every change your scripts make. For example, a script might change a Point light to an Area light, add a cookie or turn on shadows that were not configured in the scene.

For those setups, open **Shader Stripping**, disable **Auto**, and enable every feature your scripts need. Or disable **Shader Stripping** to keep all features. Test the interaction in Play Mode. The Edit Mode preview keeps all features.

Stripping is disabled in projects with the VRChat Avatar SDK. See [Shader Feature Stripping](./ForDevelopers.md#shader-feature-stripping) for the complete controls.

## Spawn Lights From Prefabs

Configure the light as a prefab with its normal **Light Volume** or **Point Light Volume** component.

A spawned light needs the scene's **Light Volume Manager** reference to register. Assign it before activation where possible. If the prefab uses **Bake In Game**, make sure the reference is ready before its first `Start`. Assigning it later does not replay the startup bake.

For Regular Light Volumes, prepare their baked data in the scene's atlas before runtime. Instantiating a prefab does not pack new 3D textures in game. Keep any runtime-only shader features enabled as described above.

See the [UdonSharp API](./ScriptingAPI.md#udonsharp-api) for spawning and registration details.

## Bakery Tips

Select **Bakery** on the Manager before the Bakery full render. Check the Manager for warnings if automatic import or bitmask controls are unavailable in your Bakery version.

Compressed Bakery volumes are not supported. When baking Light Volumes, **Compress volumes** is automatically disabled in the bake settings and any assigned preset.

Use **Volume Bitmask** and **Probe Bitmask** when different Bakery light groups should contribute to Light Volumes and ordinary probes. Keep the masks consistent with the Bakery lights' masks.

Enable **Fix Light Probes L1** if Bakery's ordinary probes look burned out or excessively dark. It reduces ringing at the cost of some contrast. This affects fallback probes, not your volume texture resolution.

If the Light Volume Inspector reports limited rotation support, update Bakery. Runtime movement rotates the stored lighting, while baking rotation support depends on the installed Bakery version.

## Match Avatar Brightness To The World

**Poiyomi** and **lilToon** can limit an avatar's minimum and maximum brightness. Enable **Force Scene Lighting** on the **Light Volume Manager** when you want avatars to follow your scene's lighting without those limits.

This uses the shared `_UdonForceSceneLighting` standard supported by these shaders. See [lil's original proposal](https://x.com/lil_xyzw/status/1961487430256922928). Use it when your scene lighting is ready to determine avatar brightness, including in dark areas.

## Check Before Release

Walk through the busiest lit areas in a test build. Check the same views with mirrors on, with the intended number of active lights, and on each target platform.

Use [Debugging Light Volumes](./HowToUse_Debugging.md) to inspect coverage, stored lighting and the data reaching shaders.

- Move a compatible avatar or prop through volume edges and lighting changes.
- Check an avatar without Light Volume support to verify ordinary probe lighting.
- Toggle lights, spawn prefabs, change projections and test other scripted lighting features.

## Migrating from 2.x.x to 3.x.x

Back up or commit your project before updating the package.

In projects with UdonSharp, old scene components migrate automatically when you open the scene. Components with missing or conflicting references are left unchanged and reported in the Console.

1. Open a scene and wait for migration and Udon compilation to finish.
2. Check the Console. If a migration warning says components were left unchanged, resolve the reported references before saving. Do not delete those components to clear the warning.
3. Inspect the **Light Volume Manager**'s volume and light lists. Test the lighting, including any moved volumes, Additive volumes and shadowed lights you use.
4. Save the scene once the result is correct. Repeat for the other scenes you use.

The **Light Volume Manager**, **Light Volume** and **Point Light Volume** Inspectors now contain the settings that previously lived on separate helper components.
