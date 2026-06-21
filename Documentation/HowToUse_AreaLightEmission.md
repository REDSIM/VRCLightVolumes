[VRC Light Volumes](../README.md) | **How to Use** | [Best Practices](../Documentation/BestPractices.md) | [Udon Sharp API](../Documentation/UdonSharpAPI.md) | [For Shader Developers](../Documentation/ForShaderDevelopers.md) | [Compatible Shaders](../Documentation/CompatibleShaders.md)

# How to Use

| Menu |
|----|
|[VRC Light Volumes System](../Documentation/HowToUse.md)|
|[Regular Light Volumes](../Documentation/HowToUse_RegularLightVolumes.md)|
|[Point Light Volumes](../Documentation/HowToUse_PointLightVolumes.md)|
|[Point Light Volume Shadows](../Documentation/HowToUse_Shadows.md)|
|**Area Light Emission**<br />&bull; [What It Does](#What-It-Does)<br />&bull; [Quick Setup](#Quick-Setup)<br />&bull; [Texture Sources](#Texture-Sources)<br />&bull; [Runtime Updates](#Runtime-Updates)<br />&bull; [Old Shader Fallback](#Old-Shader-Fallback)<br />&bull; [Performance Notes](#Performance-Notes)<br />&bull; [TVGI Legacy](#TVGI-Legacy)|
|[Audio Link Integration](../Documentation/HowToUse_AudioLinkIntegration.md)|
|[TV Screens Integration](../Documentation/HowToUse_TVScreensIntegration.md)|
|[How Light Volumes Work?](../Documentation/HowToUse_HowItWorks.md)|

## Area Light Emission

Area Light Emission lets an **Area Light** use a texture, RenderTexture or Material as a textured emitter. It is intended for TV screens, emissive signs, windows, soft boxes, LED panels and any rectangular surface where the emitted color is not uniform.

Unlike the old TVGI workflow, this does not need a separately baked additive Light Volume just to follow a screen color. The Area Light projects the source texture directly through the Point Light Volumes system, so it can stay movable, scalable and runtime-updated.

## What It Does

With a cookie assigned, an Area Light behaves like a textured rectangular emitter:

- Close to the light, the projection follows the Area Light size, proportions and local texture detail.
- Farther away, the shader samples coarser mip levels so the result gradually converges to the average emitted color.
- Near grazing angles and projection edges, the shader blends toward blurrier mip levels to approximate color mixing from a visible area emitter.
- Alpha is treated as an emission mask: the shader uses `RGB * Alpha`.

This is still an optimized diffuse lighting approximation, not full path-traced GI. It does not create real screen reflections, and it does not bounce light multiple times. For matte walls, floors, props and avatars using VRC Light Volumes compatible shaders, it is usually the better realtime option for screen-like emitters.

If an Area Light has no Cookie assigned, it keeps the original fast parametric Area Light path with no extra texture sampling.

## Quick Setup

1. Create a **Point Light Volume**.
2. Set `Type` to `Area Light`.
3. Scale the transform to match the physical size of the emitting surface.
4. Rotate the Area Light so the rectangle faces the area you want to illuminate.
5. Assign a source to the `Cookie` field. It can be a Texture, RenderTexture or Material.
6. Set `Color` and `Intensity`. These multiply the emitted texture color and can be changed at runtime without rebuilding the texture array.
7. Set `Cookie Resolution` in **Light Volume Setup** as low as acceptable for the visible result.
8. Enable `Debug Range` to check how much scene area the light affects.

For static pictures, regular Texture assets are the cheapest source. For video screens, assign the same RenderTexture used by the video player, or use a Material that renders the desired emissive image.

> [!IMPORTANT]
> World surfaces and props must use a shader with VRC Light Volumes support to receive Area Light Emission. Default Unity shaders will not show Point Light Volumes.

## Texture Sources

The `Cookie` field accepts:

- **Texture** - best for static emissive panels, signs, windows and baked graphics.
- **RenderTexture** - best for video players, cameras or any other runtime-rendered image.
- **Material** - useful when a shader generates the emission procedurally or combines textures before projection.

Area Light cookies are packed into the shared Point Light Volume texture array. The manager creates a mip chain for that array when at least one Area Light cookie is present, and the shader uses that mip chain for the softened emitter approximation.

Texture compression artifacts are still visible after packing. For important cookies, disable compression and prefer HDR-capable source formats when you need values above 1. The packed runtime array uses a linear half precision format.

The same Texture, RenderTexture or Material source can be reused by several Area Lights. The source is uploaded once, while each light still keeps its own `Color`, `Intensity`, transform and range data.

## Runtime Updates

Changing `Color`, `Intensity`, enable state or transform data does not require rebuilding the texture array. The manager updates the light data separately.

Changing the `Cookie` source, changing `Cookie Resolution`, or adding/removing a light that uses a new source requires the custom texture array to be rebuilt. The editor does this automatically from the authoring component. In runtime scripts, call `ReinitializeCustomTextures()` after changing projection sources manually.

RenderTexture and Material sources are treated as animated sources. To refresh them at runtime, enable `Auto Update Textures` in **Light Volume Setup**. Keep it disabled when all projection sources are static.

## Old Shader Fallback

Modern shaders that include the current VRC Light Volumes code sample the textured Area Light directly.

Older VRC Light Volumes shaders that do not know about Area Light cookies still get a fallback: the manager reads the final mip level from the packed cookie texture array through GPU readback, multiplies it by the light `Color` and `Intensity`, and writes that average color into the regular Point Light Volume color data.

The fallback cannot show texture detail, but it prevents old shaders from turning the light black. They receive a normal Area Light using the average emitted color instead.

Default Unity shaders still do not receive Point Light Volumes or this fallback.

## Performance Notes

Area Light cookies are heavier than no-cookie Area Lights because each visible light needs extra texture samples. They are still designed to be practical for a small number of important screen or panel lights.

For best performance:

- Avoid many overlapping Area Light cookies.
- Keep `Cookie Resolution` as low as the use case allows.
- Disable `Auto Update Textures` unless a RenderTexture or Material source really changes at runtime.
- Prefer static Texture sources for static emitters.
- Use regular baked Light Volumes when the lighting is completely static and does not need runtime control.

No-cookie Area Lights keep the faster attenuation path, so leaving the `Cookie` field empty is still the right choice for plain soft boxes.

## TVGI Legacy

The old **LightVolumeTVGI** tool is now mostly a legacy workflow. It is still useful for existing worlds and for setups that intentionally drive baked additive Light Volumes from a single average screen color.

For new TV screens, monitors, emissive signs and other rectangular animated emitters, prefer **Area Light Emission**. It preserves local texture color near the emitter, blends toward the average color with distance, supports RenderTexture and Material sources, and has an old-shader average-color fallback built into the manager.
