[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [UdonSharp API](./UdonSharpAPI.md) | [Unity Editor API](./UnityEditorAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Area Light Emission

**Guides:** [Overview](./HowToUse.md) · [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) · [Point Light Volumes](./HowToUse_PointLightVolumes.md) · [Froxel Clustering](./HowToUse_FroxelClustering.md) · [Shadows](./HowToUse_Shadows.md) · [Material Sources](./HowToUse_PointLightMaterialSources.md) · **Area Light Emission** · [AudioLink](./HowToUse_AudioLinkIntegration.md) · [TV Screens (Older Workflow)](./HowToUse_TVScreensIntegration.md) · [Debugging](./HowToUse_Debugging.md) · [How It Works](./HowToUse_HowItWorks.md)

An **Area Light** can use an image as its emitting surface. Use this for a TV casting colored light onto the room, an animated LED panel, a sign or a window. A video screen can illuminate nearby surfaces without baking a separate additive Light Volume.

Near the emitter, different parts of the image contribute different colors. Farther away, the colors blend toward the image's average. This approximates the light from a rectangular surface. It does not reproduce the screen image as a mirror reflection or calculate multiple light bounces.

![A red and blue screen casting separate colors onto the nearby floor and mixed purple light farther away](./Images/area-screen.png)

Near the screen, the floor receives separate red and blue contributions. Farther away, they blend toward magenta.

## Make A Screen Light The Room

1. Create a **Point Light Volume** and set **Type → Area Light**.
2. Place its origin at the center of the screen. Set Transform X/Y scale to the screen's width/height in meters. With an unscaled parent, a 2 m by 1 m screen uses `(2, 1, 1)`.
3. Point its blue local Z axis out of the screen toward the room. The light emits from this side only.
4. Leave **Cookie** empty first. Set **Color** to white and adjust **Intensity** until a nearby wall or prop with a compatible shader is lit.
5. Assign a static image to **Cookie** and check that its colors appear in the light. For video, replace it with the player's output Render Texture once this test works. Keep Color white to preserve the source colors.
6. Leave **Auto Update Textures** enabled on the Manager for video or other animated sources, then enter Play Mode with the video playing. Nearby surfaces should change color with the image.
7. Enable **Debug Range** and check the affected area. Add [shadows](./HowToUse_Shadows.md) if the light should be blocked by walls or furniture.
8. Enable **Dynamic** and Manager **Auto Update Volumes** if the screen will move, rotate or resize in game.

The light does not create a visible screen mesh or configure the video player. Keep your normal screen material, and give the light the same image source. If the player has no directly usable Render Texture, a [Material source](./HowToUse_PointLightMaterialSources.md) can generate or copy the emission image.

A Material source must read the player's actual image source. Assigning the screen's Material does not copy texture overrides stored on its Renderer through a Material Property Block. If the screen works but this source is blank or frozen, check how the player supplies its video texture.

<details>
<summary>Inspector for the screen example</summary>

![Area Light Inspector with a 4 by 2 metre Transform, white Color and a red-blue image assigned to Cookie](./Images/area-inspector.png)

The **Cookie** uses the same image as the visible screen. Transform X/Y scale matches the screen's **4 × 2 m** size; its blue local Z axis points toward the room.

</details>

## Choose A Source

| Cookie source | Use it for | Updates |
| --- | --- | --- |
| **Texture** | Static signs, windows, artwork. | Copied when the shared array is built. |
| **Render Texture / Custom Render Texture** | Video players, cameras, animated textures. | Refreshed with Auto Update Textures. |
| **Material** | Procedural animation or a composed image. | Pass 0 is rendered with Auto Update Textures. |
| **None** | A plain soft box with one uniform color. | No cookie texture sampling. |

RGB gives the emitted color; **alpha masks emission**. A bright image with zero alpha emits no light. This is a common reason for a Render Texture looking correct on the screen while the Area Light remains dark.

Negative Transform X/Y scale mirrors the cookie on the corresponding axis, including mirrored parents. Match the light's orientation to the screen image if the colors appear reversed.

## Quality And Performance

**Cookie Resolution** on the Manager controls all Point Light Volume projection textures, including Area cookies. Try a lower resolution first: a soft wash of screen light needs fewer pixels than the screen itself. The system creates the blurred, smaller texture levels it needs; the source does not need its own mipmaps.

Avoid lossy compression when it creates visible blocks or color bands. HDR textures and Materials can supply colors above `1`; the runtime cookie array uses half precision and preserves HDR values within that format's range.

Several Area Lights can share the same source. This saves texture storage, but every overlapping light still adds shading work. Use separate Material objects only when the generated images need different parameters.

For steady, uniform light, leave Cookie empty. For lighting that never changes, [bake a Regular Light Volume](./HowToUse_RegularLightVolumes.md). Keep live textures and overlapping Area lights for emitters whose changing appearance matters.

## If The Result Looks Wrong

| Symptom | Check |
| --- | --- |
| No light | The receiving shader supports Light Volumes, the blue Z axis faces the room, Color/Intensity are nonzero, and cookie alpha is nonzero. |
| Image is frozen | The source itself is updating and Manager Auto Update Textures is enabled. A runtime API snapshot stays fixed until the next rebuild. |
| Everything gets one average color | Move closer to the emitter. Also check whether the receiving shader has only the older 2.x integration. |
| Color is reversed | Match Transform orientation and X/Y scale signs to the screen. |
| Light passes through walls | Enable and bake the light's shadows, or reduce its range if those surfaces should be outside it. |
| Works in Edit Mode but disappears in game | Check the Manager's Shader Stripping settings if scripts introduce Area cookies at runtime. |

## Older Shader Support

Current integrations show textured Area emission. A **2.x-compatible shader that already supports Area Lights** receives one average cookie color instead. This keeps the screen light visible, but loses image detail and orientation. Default Unity shaders receive neither path.

[LightVolumeTVGI](./HowToUse_TVScreensIntegration.md) remains useful for a different effect: tinting pre-baked additive lighting with one average screen color. Use that when the baked bounce pattern is part of the intended result.

## Runtime Changes

Use `SetColor()` and `SetIntensity()` to change brightness or tint. For a moving screen, Dynamic plus Auto Update Volumes tracks its transform. If you manage transforms manually, call `UpdateTransform()` after moving or rotating and `UpdateScale()` after a scale-only change.

Use `SetCustomTexture(texture)` or `SetCustomMaterial(material)` to replace the Cookie and request the required array rebuild. To keep a snapshot, use `SetCustomTexture(texture, false, false)` or `SetCustomMaterial(material, false)`. Reuse an existing source and update its contents for continuous animation; repeatedly swapping sources causes array rebuilds.

See the [UdonSharp API](./UdonSharpAPI.md#pointlightvolumeinstance) for exact signatures.
