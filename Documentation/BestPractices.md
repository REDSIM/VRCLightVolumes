[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | **Best Practices** | [UdonSharp API](./UdonSharpAPI.md) | [Unity Editor API](./UnityEditorAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Best Practices

Start with baked room lighting, add runtime lights where you need control, then test the busiest part of the world on the intended device. These tips help decide what to change when lighting looks wrong or costs too much.

## Choose The Lighting Type For The Job

| Situation | A useful starting point |
| --- | --- |
| Room lighting, sunlight and many stationary lamps | Bake them into **Regular Light Volumes**. Keep lightmaps for surfaces that need detailed baked shadows. |
| A group of lamps that switches as one | Bake an **Additive Light Volume** separately from the base lighting. |
| Flashlight or projector | A **Spot Light Volume**. A narrow cone avoids lighting unrelated parts of the scene. |
| Portable bulb or independently animated lamp | A **Point Light Volume**. |
| Screen, sign or soft panel | An **Area Light Volume**; add a texture when its image should affect the light. |
| Small props with visible lightmap seams | Try a compatible material lit by the regular volume instead of a lightmap. Check that the grid has enough detail for the prop. |
| Lit particles or fog meshes | Use a compatible particle shader. Keep the number of overlapping transparent layers low. |

A large number of stationary lights can share one baked volume. Their number does not become a runtime light loop. Use Point Light Volumes when their separate runtime controls are useful.

Use a Point Light Volume's **Bake Into Probes** only for lights that should remain in the ordinary probe lighting. Switching that light off in game does not remove its already-baked contribution from Unity Light Probes.

## Spend Voxel Detail Locally

Start with one coarse volume for broad lighting, then use smaller, denser volumes for sharp shadows or strong color changes. Avoid covering empty sky, underground space or inaccessible parts of the world with a dense grid.

Use **Preview Voxels** to check placement, then judge the bake on a moving prop. More voxels increase memory and bake work; they are useful only when they preserve visible lighting detail. Doubling density on all three axes creates about eight times the data.

On the Manager, keep **Denoise** enabled for a first bake. If a clean bake loses too much fine detail, compare with it disabled before increasing the entire volume's resolution. For Progressive bakes, **Dilate Invalid Probes** helps replace unusable samples inside geometry with nearby valid lighting.

**Downscale Volumes** on the Manager reduces the packed atlas resolution. It is useful for comparing lower-memory versions of an existing bake. Check small shadows and doorway transitions after downscaling.

Give each volume you bake a unique name. Duplicates that reuse baked data with **Bake** disabled can share textures.

## Hide Room-To-Room Seams

Overlap neighboring volumes by more than their **Smooth Blending** width. For example, try a `0.5 m` overlap with a `0.25 m` blend region, then move a test prop through the doorway.

Set **Weight** in the Manager's **Light Volumes** list. Keep a broad fallback volume at a low weight and detailed room volumes at higher weights.

The Manager's three-dot menu contains **Sort Light Volumes**. It preserves weights and sorts equal-weight volumes by resolution settings: manual resolution first, then higher **Voxels Per Unit**. Assign different weights when a particular overlap must have an explicit priority.

For edges adjoining uncovered areas, enable **Light Probes Blending** and disable **Sharp Bounds**. Extend the volume beyond the area that needs its full lighting, because the outer edge now fades toward the ordinary probes.

See [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) for a placement diagram and additive bake example.

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

The Manager's **Froxel Clustering > Clustering Enabled** option lets shaders skip Point Light Volumes that cannot reach a small region of the camera view. It is enabled by default and only runs once the active light count reaches **Min Lights Count**.

Start with the defaults and compare clustering on and off in your scene. It is most useful when many lights occupy different rooms or small areas. It helps less when all lights illuminate the same visible surface.

**Angular Density** divides the view more finely left-to-right and top-to-bottom; **Slices Count** adds divisions in depth. Increasing either can reject more unrelated lights, but costs more memory and work to build the grid. Use the [Froxel Clustering guide](./HowToUse_FroxelClustering.md) to inspect the grid before tuning.

**Shadow Culling** is a separate option, off by default. Test it for scenes where baked shadows hide whole areas from many lights. Keep it only if it improves performance in your build.

Clustering does not optimize Regular or Additive Light Volumes. Check mirrors and secondary-camera views when testing, because they may use the ordinary light loop.

## Limit Excessive Overlap

**Additive Max Overdraw** on the Manager limits how many additive volume samples and how many Point Light Volume contributions a pixel can receive. The two groups use separate caps with the same value.

For example, a value of `4` allows up to four additive volume samples and up to four counted Point Light contributions; it is not a single four-light limit shared by both groups.

Lowering it can improve the worst case, but lights may disappear where the cap is reached. Some lights still consume a slot before a cookie or shadow makes their final contribution black. Choose the smallest value that preserves the intended result and test the most crowded overlap.

## Choose How Shadows Update

| What changes? | Shadow workflow |
| --- | --- |
| Light and shadow-casting geometry stay still | **Bake Shadows** in the Editor. Avatars can receive these shadows as they move through the scene. |
| Geometry needs to be captured once after joining | **Bake In Game**. Each light requests one bake at startup; the Manager processes one queued light per frame. |
| A light or shadow caster moves and the shadow must follow | **Point Light Shadow Runtime Baker**, rebaking when needed or continuously. |

**Bake In Game** does not continuously track changes. It leaves the editor preview shadow out of the build, so the light has to finish its runtime bake before that shadow is available.

Baked shadow maps still cost memory and shader work, but do not require a camera to render the scene every frame. Continuous shadow baking adds scene rendering and filtering; reserve it for lights where changing shadows matter.

For cheaper shadow maps:

- Prefer Spot lights with a moderate cone. Below `180°`, a Spot light normally needs one shadow image; Point and Area lights need six.
- Keep **Force Cubemap Shadows** off for narrower cones. Enable it when the Inspector's Spot **Angle** is `180°` or more, so the shadow covers the full cone.
- Use the smallest acceptable **Shadow Resolution** on the Manager. The light's **Shadows > Resolution** can override its bake resolution; the runtime atlas still uses the Manager resolution.
- Keep **Near Plane** close enough to include nearby walls. Set **Far Plane** to `0 (Auto)` unless a deliberate fixed capture range is needed.
- Increase **Bias** just enough to remove self-shadow speckles. Too much separates the shadow from the object.
- Try disabling **Spherical Blur** if faster planar blur looks acceptable. Restore it if you see cubemap seams or Spot projection-edge artifacts.

Continuous runtime shadows are usually a poor starting point for Quest. Begin with baked shadows and verify any required runtime baker on the headset. See [Shadows](./HowToUse_Shadows.md) for setup and troubleshooting.

## Check Mobile Shadow Quality Separately

The package uses lower-precision shadow textures on mobile targets than on PC. Test with the intended build target selected and verify on the device.

For mobile speckles or noisy shadow edges, start with the Manager's mobile **Shadow Min Variance** at `1` and try **Shadow Bleed Reduction** around `0.2–0.4`. Compare against the defaults in the same view: stronger settings can change thin shadows and contact edges.

Use per-light **Bias** for self-shadow artifacts and **Blur** for softness. These are bake settings, so rebake after changing them.

## Keep Animated Sources Affordable

Use the smallest acceptable **Cookie Resolution**. A Point light's cubemap needs six images; a Spot cookie or an Area emitter texture uses one.

If a Material can generate the same image as a Custom Render Texture, the Material source avoids extra copying. Use a simple Unlit material unless lighting is part of the intended image.

The Manager's **Auto Update Textures** updates sources marked for live updates. A static texture normally stays unchanged between atlas rebuilds; RenderTextures, Custom Render Textures and Materials normally update live. For a scripted source that should freeze, use the projection setter overload with `autoUpdate = false`.

See [Material Sources](./HowToUse_PointLightMaterialSources.md) and [Area Light Emission](./HowToUse_AreaLightEmission.md) for setup.

## Update Only What Changes

Enable **Dynamic** for volumes whose position, rotation or scale changes in game, and enable the Manager's **Auto Update Volumes** to follow those changes automatically.

Color, intensity and active-state changes do not require transform polling. Use the component's [UdonSharp setters](./UdonSharpAPI.md), such as `SetColor()` and `SetIntensity()`, when controlling lights from scripts.

Disable a zone's Light Volume GameObjects when their lighting cannot affect the player or visible objects. Include what can be seen through doors and in mirrors before turning a zone off. Disabling volumes reduces rendering work, but does not unload their baked textures from the shared atlas.

Use GameObject toggles for zone changes. For frequently blinking lights, animate intensity instead of repeatedly removing and adding the objects to the Manager.

Regular and Additive Light Volumes share **32 active slots**; Point/Spot/Area Light Volumes share **128**. Treat these as limits, not performance targets.

## Preserve Features Used By Scripts

The Manager's **Shader Stripping** removes unused lighting code from Play Mode and world builds. With **Auto** enabled, it detects configured scene features, including those on inactive or zero-intensity lights.

It cannot predict every change your scripts make. For example, a script might change a Point light to an Area light, add a cookie or turn on shadows that were not configured in the scene.

For those setups, open **Shader Stripping**, disable **Auto**, and enable every feature your scripts will need. Alternatively, disable **Shader Stripping** to keep all features. Test the actual interaction in Play Mode: Edit Mode always keeps all features, so an Editor preview alone will not reveal missing build features.

Stripping is disabled in projects with the VRChat Avatar SDK. See [Shader Feature Stripping](./ForDevelopers.md#shader-feature-stripping) for the complete controls.

## Spawn Lights From Prefabs

Configure the light as a prefab with its normal **Light Volume** or **Point Light Volume** component. Do not follow old 2.x instructions that remove the authoring component or change an **Is Initialized** flag.

A spawned light needs the scene's **Light Volume Manager** reference to register. Assign it before activation where possible. If the prefab uses **Bake In Game**, make sure the reference is ready before its first `Start`; assigning it later does not replay the startup bake.

For Regular Light Volumes, prepare their baked data in the scene's atlas before runtime. Instantiating a prefab does not pack new 3D textures in game. Keep any runtime-only shader features enabled as described above.

See the [UdonSharp API](./UdonSharpAPI.md) for spawning and registration details.

## Bakery Tips

Select **Bakery** on the Manager before the Bakery full render. Check the Manager for warnings if automatic import or bitmask controls are unavailable in your Bakery version.

Use **Volume Bitmask** and **Probe Bitmask** when different Bakery light groups should contribute to Light Volumes and ordinary probes. Keep the masks consistent with the Bakery lights' masks.

Enable **Fix Light Probes L1** if Bakery's ordinary probes look burned out or excessively dark. It reduces ringing at the cost of some contrast. This affects fallback probes, not your volume texture resolution.

If the Light Volume Inspector reports limited rotation support, update Bakery. Runtime movement rotates the stored lighting, while baking rotation support depends on the installed Bakery version.

## Match Avatar Brightness To The World

Some avatar shaders apply their own minimum or maximum brightness. If a compatible avatar looks too bright in a dark room, try **Force Scene Lighting** on the Manager. Supporting shaders then use the scene lighting without those brightness limits. This option does not add Light Volume support to an incompatible shader.

## Check Before Release

Walk through the busiest lit areas in a test build. Check the same views with mirrors on, with the intended number of active lights, and on each target platform.

Use [Debugging Light Volumes](./HowToUse_Debugging.md) to inspect coverage, stored lighting and the data reaching shaders.

- Move a compatible avatar or prop through volume edges and lighting changes.
- Check an avatar without Light Volume support to verify ordinary probe lighting.
- Toggle lights, spawn prefabs, change projections and test other scripted lighting features.
- Inspect the Manager's **Data size in VRAM** and **Data size in bundle** estimates. Treat them as estimates; runtime and build measurements are the final check.
- If performance drops, change one factor at a time: overlapping lights, shadow updates, source updates or clustering settings. Compare the same view after each change.

For custom shaders, use [Shader Integration](./ForDevelopers.md) to choose between diffuse lighting, individual glossy highlights and simpler particle lighting.
