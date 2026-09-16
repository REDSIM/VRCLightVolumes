[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [UdonSharp API](./UdonSharpAPI.md) | [Unity Editor API](./UnityEditorAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Point Light Volumes

**Guides:** [Overview](./HowToUse.md) · [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) · **Point Light Volumes** · [Froxel Clustering](./HowToUse_FroxelClustering.md) · [Shadows](./HowToUse_Shadows.md) · [Material Sources](./HowToUse_PointLightMaterialSources.md) · [Area Light Emission](./HowToUse_AreaLightEmission.md) · [AudioLink](./HowToUse_AudioLinkIntegration.md) · [TV Screens (Older Workflow)](./HowToUse_TVScreensIntegration.md) · [Debugging](./HowToUse_Debugging.md) · [How It Works](./HowToUse_HowItWorks.md)

![Point, Spot and Area Light Volume examples.](./Preview_4.png)

Point Light Volumes are lights you can move, recolor and switch on or off while the world is running. The same component provides Point, Spot and Area lights. They work without baking a Regular Light Volume, but receiving materials must use a [compatible shader](./CompatibleShaders.md).

Use them for a flashlight, a switchable lamp, a video screen or a music-reactive light. For many lights that never change, bake their combined lighting into a [Regular Light Volume](./HowToUse_RegularLightVolumes.md).

## Add Your First Light

1. Right-click in the Hierarchy and select **Point Light Volume**, or use **GameObject → Point Light Volume**. The first light also creates a **Light Volume Manager** if the scene does not have one.
2. For a quick test, create a Material and choose **Light Volume Samples → Light Volume PBR** in its Shader dropdown. Set the material's Color to white, Metallic to `0` and Smoothness to `0`, then assign it to a sphere.
3. Keep **Type → Point Light** and **Projection → Parametric**. Place the light near the sphere, with Transform scale `(1, 1, 1)`.
4. Start with white **Color**, **Light Source Size = 0.025** and **Intensity = 100**, then adjust Intensity while looking at the sphere. No lighting bake is needed for this preview.
5. Enable **Debug Range** to check how far the light reaches. Select the light and enable Scene-view **Gizmos** to see the yellow range outline.
6. If the light will move, rotate or scale in game, enable **Dynamic** and leave **Auto Update Volumes** enabled on the Manager.

In 3.0, configure the single **Point Light Volume** component. Scripts reference its class, `PointLightVolumeInstance`; there is no separate component to configure.

| Type | Use it for | Placement |
| --- | --- | --- |
| **Point Light** | Bulbs, small lamps, lights shining in every direction. | Put the origin at the emitter. |
| **Spot Light** | Flashlights, stage lights, projectors. | Point the blue local Z axis toward the lit area; adjust **Angle** and **Falloff**. |
| **Area Light** | Screens, windows, soft boxes, rectangular panels. | Match Transform X/Y scale to the rectangle's width/height in meters. The blue local Z axis faces the lit side. |

An Area Light emits from one side. If it appears dark, check its orientation before increasing Intensity. Area lights cost more than Point and Spot lights, so use them where the rectangular source matters.

## Size, Brightness And Range

![Light placement and affected ranges.](./Preview_5.png)

The yellow outline shows the culling range, including dim lighting beyond the bright patch beside the source.

For Point and Spot lights, **Light Source Size** is the physical radius of the emitter. Transform scale also scales this radius. A small bulb needs a small size; a larger source produces broader specular highlights. Area lights use the rectangle's width and height instead.

Set the source size first, then adjust **Intensity**. Values in the hundreds or thousands are normal for small sources; these values do not match Unity's built-in Light intensity scale.

The range is calculated from source size, scale, Color, Intensity and the Manager's **Brightness Cutoff**. Larger or brighter sources reach farther. Raising Brightness Cutoff shortens the range of all Point Light Volumes, which can improve performance but removes dim lighting at the edges. LUT projection is the exception: it exposes a manual **Range**.

Keep **Debug Range** enabled while placing lights. Avoid a small room lamp reaching several neighboring rooms. Shadows hide light behind walls, but do not make a large range free to evaluate.

**Shading Strength** controls extra surface-normal shading and shadow strength. Leave it at `1` for normal lighting. At `0`, that extra shading and the light's shadows are disabled.

Individual size-aware highlights need a shader using `LightVolumeSHSpecular()` or the ASE **Light Volume SH Specular** node. Older integrations can still show the light with an approximate highlight.

## Projection Modes

### Parametric

![Parametric Spot Light and its cone.](./Preview_7.png)

Start with **Parametric** for ordinary Point and Spot lights. It calculates the light's distance falloff automatically.

For a Spot Light, **Angle** is the full cone angle in degrees and **Falloff** softens its edge. Parametric angles can exceed 180 degrees for an inverted cone. This does not apply to a custom cookie projector.

### LUT

![LUT textures and the light shapes they produce.](./Preview_6.png)

Use **LUT** when you need to draw your own distance falloff or Spot cone profile. A LUT is a small texture that describes the light:

- Horizontal axis: color and brightness from the center to the edge of a Spot cone.
- Vertical axis: color and brightness over distance.
- Point lights use only the vertical axis.

Assign it to **Falloff LUT** and set **Range** manually. This is useful for stylized falloff and ring-shaped stage lights.

### Custom

![Colored cookies and cubemap projection.](./Preview_8.png)

Use **Custom** to project an image:

- **Spot Light:** assign a 2D image to **Cookie**. RGB supplies the color and alpha masks the light. Set **Spot Cookie Aspect** to image width divided by height; `1` is square.
- **Point Light:** assign a **Cubemap**, for example a star projector or disco-ball pattern. RGB supplies the color; alpha is ignored.
- **Area Light:** assign **Cookie** directly; it has no Projection dropdown. See [Area Light Emission](./HowToUse_AreaLightEmission.md).

Sources can also be Render Textures or [Materials that generate an image](./HowToUse_PointLightMaterialSources.md). Use a static texture for an unchanging pattern. Disable lossy compression if it creates visible bands or blocks in the projected light.

## Projection Texture Cost

The Manager packs cookies, LUTs and cubemaps into a shared texture array. **Cookie Resolution** sets the resolution of every slice; source images are resized to fit.

- A 2D image uses one slice; a cubemap uses six.
- Lights sharing the same source and update mode can share its stored image.
- Static textures are copied when the array is built or rebuilt.
- Render Textures, Custom Render Textures and Materials update while **Auto Update Textures** is enabled. They default to live updates; the API also supports a fixed snapshot.

Try a lower Cookie Resolution and compare the visible result before increasing it. A soft screen glow usually needs less detail than a sharply projected logo. Doubling resolution uses four times as much memory per slice.

## Shadows

Under **Shadows**, enable **Enabled**, then choose a workflow:

- **Bake Shadows:** save shadows of stationary geometry in the Editor. You can still change the light's color and intensity.
- **Bake In Game:** capture the geometry once when the light first starts in game.
- **Point Light Shadow Runtime Baker:** rebake on activation or update continuously for moving lights or objects.

A normal Spot shadow renders one view. Point and Area shadows render six views, as does a Spot with **Force Cubemap Shadows** enabled. For Spot angles of 180 degrees or more, enable Force Cubemap Shadows.

Start with baked shadows for fixed lamps and a single-view Spot for a moving flashlight. See [Shadows](./HowToUse_Shadows.md) for setup, quality controls and runtime costs.

## Bake Into Probes

Enable **Bake Into Probes** for a static light that should also affect ordinary Unity Light Probes. This helps objects and avatars whose shaders do not support Light Volumes. Re-bake the scene's probes after changing that light.

Leave it off for lights that move or change: ordinary baked probes will keep the old lighting when the live light is switched off or recolored.

## Runtime Control

Use the `PointLightVolumeInstance` setters, such as `SetColor()`, `SetIntensity()`, `SetDynamic()` and the type/projection setters in the [UdonSharp API](./UdonSharpAPI.md#pointlightvolumeinstance). Color, intensity and enable-state changes do not need Auto Update Volumes. Moving transforms do.

If scripts introduce a light type, cookie or shadows that are absent from the authored scene, retain those features in the Manager's **Shader Stripping** settings. Automatic detection cannot predict later script changes.

## Keep It Fast

Keep the number of overlapping lights low. There can be at most **128 active Point Light Volumes**, and **Additive Max Overdraw** separately limits Point Light Volume work at each pixel. A light can disappear when these limits are reached; raising the cap increases shader work.

A disabled GameObject/component, zero Intensity or black Color removes the light from the active list. Use that for rooms or effects that are not needed. `SetWeight()` gives important lights priority when limits are reached.

[Froxel Clustering](./HowToUse_FroxelClustering.md) helps scenes with many lights in different places. It does less for many large lights covering the same surface. Bake static lighting, keep ranges tight, and profile the busiest view on the target device.
