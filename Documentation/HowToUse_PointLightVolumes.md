[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [Scripting API](./ScriptingAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Point Light Volumes

| Menu |
| --- |
| [Overview](./HowToUse.md) |
| [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) |
| **Point Light Volumes**<br />• [Setup Point Light Volumes](#setup-point-light-volumes)<br />• [Size, Brightness And Range](#size-brightness-and-range)<br />• [Projection Modes](#projection-modes)<br />• [Area Light Cookies](#area-light-cookies)<br />• [Animated Textures](#animated-textures)<br />• [Shadows](#shadows)<br />• [Bake Into Probes](#bake-into-probes)<br />• [Runtime Control](#runtime-control)<br />• [Keep It Performant](#keep-it-performant) |
| [Froxel Clustering](./HowToUse_FroxelClustering.md) |
| [Shadows](./HowToUse_Shadows.md) |
| [Material Sources](./HowToUse_PointLightMaterialSources.md) |
| [AudioLink](./HowToUse_AudioLinkIntegration.md) |
| [TV Screens](./HowToUse_TVScreensIntegration.md) |
| [Debugging](./HowToUse_Debugging.md) |
| [How It Works](./HowToUse_HowItWorks.md) |

![Point, Spot and Area Light Volume examples.](./Preview_4.png)

Point Light Volumes are custom realtime Point, Spot and Area lights, similar to Unity's built-in lights. They offer physically based falloff, features such as animated cookies and realtime Area lighting, and can be more efficient in scenes with many lights.

They work separately from Regular Light Volumes and do not store lighting in voxels. You can move them, change their color, or switch them on and off in game.

> [!WARNING]
> Materials need a [shader that supports VRC Light Volumes](./CompatibleShaders.md) to receive this light directly. For static lights, [Bake Into Probes](#bake-into-probes) can store their lighting in ordinary Unity Light Probes. This requires enough probes in the lit areas and materials that use Light Probes.

Use Point Light Volumes for flashlights, switchable lamps, video screens or music-reactive lights. For many lights that never change, use your lightmapper's lights and bake them into [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) instead.

<a id="add-your-first-light"></a>

## Setup Point Light Volumes

1. Right-click in the Hierarchy and select **Point Light Volume**. A **Light Volume Manager** is added if the scene does not have one.
2. Choose **Point Light**, **Spot Light** or **Area Light** in **Type**, depending on the source's shape.
3. Position the light and match its **Light Source Size** to the emitting surface. Then adjust **Color** and **Intensity**.
4. If the light will move, rotate or scale in game, enable **Dynamic** on it and **Auto Update Volumes** on the Manager.

| Light&nbsp;Type | Use it for | Source Size |
| --- | --- | --- |
| **Point&nbsp;Light** | Bulbs, lamps, light in all directions. | Set **Light Source Size** to the approximate radius of the bulb or emitting surface, in meters. |
| **Spot&nbsp;Light** | Flashlights, spotlights, projectors. | Match **Light Source Size** to the lens or reflector radius, in meters. Set **Angle** to match the beam's spread. |
| **Area&nbsp;Light** | Screens, panels, soft boxes. | Set Transform X/Y scale to match the emitting surface's width and height, in meters. |


> [!IMPORTANT]
> Source size describes the emitter, not how far its light reaches. Set the size first, then adjust **Intensity**. Large sources may need low values, while small sources may need values in the hundreds or thousands.

For artistic effects, you can make the source larger or smaller than the visible emitter and adjust **Intensity** to suit.

## Size, Brightness And Range

![Light placement and affected ranges.](./Preview_5.png)

Enable **Debug Range**, select the light and turn on Scene view **Gizmos** to see its range. The yellow outline includes dim lighting beyond the bright patch.

Transform scale also changes the source size of Point and Spot lights. Larger sources produce larger specular highlights in shaders that support individual speculars.

**Color** tints the light and **Intensity** sets its brightness. Its values use a different scale from Unity's built-in Light intensity.

Larger or brighter sources reach farther. Raise the Manager's **Brightness Cutoff** to shorten calculated ranges, at the cost of dim lighting near the edges. **LUT** projection has a manual **Range** instead.

Use the range outline to check overlap between lights. Shadows do not reduce the calculated range.

**Shading Strength** controls surface shading and shadow strength. At `1` they apply fully; at `0` they are disabled.


## Projection Modes

### Parametric

![Parametric Spot Light and its cone.](./Preview_7.png)

Use **Parametric** for ordinary Point and Spot lights. It calculates the light's distance falloff automatically.

For a Spot Light, **Angle** is the full cone angle in degrees and **Falloff** softens its edge. Parametric angles can exceed 180 degrees for an inverted cone.

### LUT

![LUT textures and the light shapes they produce.](./Preview_6.png)

Use **LUT** when you need to draw your own distance falloff or Spot cone profile. A LUT is a small texture that describes the light:

- Horizontal axis: color and brightness from the center to the edge of a Spot cone.
- Vertical axis: color and brightness over distance.
- Point lights use only the vertical axis.

Assign it to **Falloff LUT** and set **Range** manually. This is useful for stylized light falloff and attenuation.

### Custom

![Colored cookies and cubemap projection.](./Preview_8.png)

Use **Custom** to project an image:

- **Spot Light:** assign a 2D image to **Cookie**. RGB supplies the color and alpha masks the light. Set **Spot Cookie Aspect** to image width divided by height; `1` is square.
- **Point Light:** assign a **Cubemap**, for example a star projector or disco-ball pattern. RGB supplies the color; alpha is ignored.
- **Area Light:** assign **Cookie** directly; it has no Projection dropdown. See [Area Light Cookies](#area-light-cookies).

## Area Light Cookies

An Area Light's **Cookie** approximates light spreading from a screen. Nearby surfaces receive different colors from the image; farther away, those colors blend together. It lights the surroundings instead of projecting a sharp picture.

For video-screen lighting, this can be a simpler, lower-cost alternative to [LTCGI](https://ltcgi.dev/) or [AreaLit](https://booth.pm/en/items/3661829). It provides only simplified, blurred specular highlights in shaders that support them, without detailed reflections of the screen image.

![A red and blue screen casting separate colors nearby and mixed purple light farther away.](./Images/area-screen.png)

For a video player, assign its output **Render Texture** to **Cookie**, or use a [Material source](./HowToUse_PointLightMaterialSources.md) that reads the video image. Enable **Auto Update Textures** on the **Light Volume Manager**. Match the light's X/Y scale to the screen and point its blue local Z axis toward the room.

Keep **Color** white to preserve the video colors. Cookie alpha masks emission, so an image with zero alpha produces no light. If the player supplies its texture through a **Material Property Block**, copying the screen Material alone will not include that texture.

See [TV Screens Integration](./HowToUse_TVScreensIntegration.md#area-light-setup) for screen alignment and shadow baking.

> [!NOTE]
> Shaders with VRC Light Volumes **3.x support** receive the cookie's different colors. Older **2.x shaders with Area Light support** receive one average color instead.

## Animated Textures

Sources can also be Render Textures or [Materials](./HowToUse_PointLightMaterialSources.md).

With **Auto Update Textures** enabled on the Manager, Light Volumes updates each animated source every frame:

- **Spot or Area cookie:** one image.
- **Point cubemap:** six faces.
- **LUT:** one image, including on Point lights.

Each image update adds at least one draw call. Material rendering and cubemap conversion can require additional draw calls.

Several Spot lights sharing one source still update only one image per frame. Several Point lights sharing one cubemap still update only one set of six faces. Assign the same Texture or Material asset to share these updates. Separate copies count as separate sources.

Higher **Cookie Resolution** means more pixels to draw every frame for animated sources, so it increases GPU cost as well as texture memory (VRAM). Static textures have no per-frame redraw, higher resolution mainly increases VRAM use.

For a fixed image, use a static Texture asset or a [snapshot](./HowToUse_PointLightMaterialSources.md#updates-and-snapshots). Materials and Render Textures are treated as live sources by default, even when their image does not change.

## Shadows

Under **Shadows**, turn on **Enabled**, then choose a workflow:

- **[Bake In Editor](./HowToUse_Shadows.md#baked-shadows):** use **Bake Shadows** to capture stationary geometry. The baked shadow maps are included in the world build.
- **[Bake In Game](./HowToUse_Shadows.md#bake-in-game):** bake once when the light first starts in game. You can still bake in the Editor for a preview, but those maps will not be in the world build. The runtime maps still use GPU memory, and baking may cause a brief stutter.
- **[Bake In Realtime](./HowToUse_Shadows.md#realtime-shadows):** add **Point Light Shadow Runtime Baker** and enable **Realtime** to update shadows continuously for moving lights or shadow casters. Use **Bake On Enable** instead for one bake each time the baker is activated. Continuous updates can be expensive, especially for six-view shadows.

A normal Spot Light captures one shadow view. Point and Area lights capture six views, as does a Spot with **Force Cubemap Shadows** enabled. Consider Force Cubemap Shadows for wide Spot angles of around 120 degrees or more.

See [Shadows](./HowToUse_Shadows.md) for setup, quality controls and runtime costs.

## Bake Into Probes

Enable **Bake Into Probes** for a static light that should also affect ordinary Unity Light Probes. Place enough probes in the lit area to capture its lighting. Objects and avatars without Light Volume support can receive this baked lighting if their materials use Light Probes. Re-bake the scene's probes after changing the light.

Leave it off for lights that move or change: ordinary baked probes keep the old lighting when the live light is switched off or changes color.

## Runtime Control

Use the [UdonSharp API](./ScriptingAPI.md#pointlightvolumeinstance) to change a light from scripts. Color, intensity and on/off changes work without **Auto Update Volumes**. Enable it with **Dynamic** for movement.

If scripts change a light type, cookie or shadows, retain those features in the Manager's **Shader Stripping** settings. Automatic detection cannot predict later script changes.

## Keep It Performant

Keep the active light count and overlap low. Use **Debug Range** to avoid lighting areas that don't need each light.

> [!IMPORTANT]
> **128 active Point Light Volumes** is a maximum, not a performance target. The Manager's **Additive Max Overdraw** also limits how many affect one pixel. Lights can be seen with visual artefacts where this limit is reached.

Disable lights in unused areas, or set their Intensity to zero. Scripts can use `SetWeight()` to give important lights priority.

Prefer baked shadows for stationary lights. And better use realtime shadows for spotlighs only, because they are ~6 times cheraper than point lights or area lights with realtime shadows.

[Froxel Clustering](./HowToUse_FroxelClustering.md) helps scenes with many lights in different places. It does less for many large lights covering the same surface. Keep ranges tight and profile the busiest view on the target device.

If shadows cover large parts of a light's range, try [**Shadow Culling** (Hi-Z)](./HowToUse_FroxelClustering.md#shadow-culling-hi-z) in the Manager's **Froxel Clustering** settings. It skips that light in fully shadowed cells. Use it with baked or one-shot runtime shadows, and compare frame time with the option on and off.
