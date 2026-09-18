[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [Scripting API](./ScriptingAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Area Light Emission

| Menu |
| --- |
| [Overview](./HowToUse.md) |
| [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) |
| [Point Light Volumes](./HowToUse_PointLightVolumes.md) |
| [Froxel Clustering](./HowToUse_FroxelClustering.md) |
| [Shadows](./HowToUse_Shadows.md) |
| [Material Sources](./HowToUse_PointLightMaterialSources.md) |
| **Area Light Emission**<br />• [Make A Screen Light The Room](#make-a-screen-light-the-room)<br />• [Choose A Source](#choose-a-source)<br />• [Quality And Performance](#quality-and-performance)<br />• [If The Result Looks Wrong](#if-the-result-looks-wrong)<br />• [Older Shader Support](#older-shader-support)<br />• [Runtime Changes](#runtime-changes) |
| [AudioLink](./HowToUse_AudioLinkIntegration.md) |
| [TV Screens (Older Workflow)](./HowToUse_TVScreensIntegration.md) |
| [Debugging](./HowToUse_Debugging.md) |
| [How It Works](./HowToUse_HowItWorks.md) |

Use an **Area Light** to cast colored light from a TV, sign, window or LED panel. Assign its image to **Cookie**; no lighting bake is needed. This lights nearby surfaces, but doesn't create a mirror image or bounced lighting.

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

Keep your screen mesh, material and video player. Give the light the same image source. If the player has no usable Render Texture, use a [Material source](./HowToUse_PointLightMaterialSources.md) that reads its video image.

Copying the screen's Material won't copy video textures assigned through a Renderer Material Property Block. If the light is blank or frozen, check how the player supplies its image.

<details>
<summary>Inspector for the screen example</summary>

![Area Light Inspector with a 4 by 2 metre Transform, white Color and a red-blue image assigned to Cookie](./Images/area-inspector.png)

The **Cookie** uses the same image as the visible screen. Transform X/Y scale matches the screen's **4 × 2 m** size; its blue local Z axis points toward the room.

</details>

## Choose A Source

| Cookie source | Use it for | Updates |
| --- | --- | --- |
| **Texture** | Static signs, windows, artwork. | Fixed image. |
| **Render Texture / Custom Render Texture** | Video players, cameras, animated textures. | Refreshed with Auto Update Textures. |
| **Material** | Procedural animation or a composed image. | Refreshed with Auto Update Textures. |
| **None** | A plain soft box with one uniform color. | Uses the light's Color. |

RGB gives the emitted color; **alpha masks emission**. An image with zero alpha emits no light, even if it looks bright on the screen.

Negative Transform X/Y scale mirrors the cookie on the corresponding axis, including mirrored parents. Match the light's orientation to the screen image if the colors appear reversed.

## Quality And Performance

Start with a low Manager **Cookie Resolution** and increase it only if the light needs more detail. Screen glow needs fewer pixels than the screen itself. The source doesn't need mipmaps.

Disable lossy compression if it creates visible blocks or color bands. HDR textures and Materials are supported.

Reuse the same source for matching lights. Use separate Materials when their image settings need to differ, and keep the number of overlapping lights low.

For steady, uniform light, leave Cookie empty. For lighting that never changes, [bake a Regular Light Volume](./HowToUse_RegularLightVolumes.md). Keep live textures and overlapping Area lights for emitters whose changing appearance matters.

## If The Result Looks Wrong

| Symptom | Check |
| --- | --- |
| No light | The receiving shader supports Light Volumes, the blue Z axis faces the room, Color/Intensity are nonzero, and cookie alpha is nonzero. |
| Image is frozen | Check that the source is playing and Manager Auto Update Textures is enabled. For scripted sources, check that live updates are enabled. |
| Everything gets one average color | Move closer to the emitter. Also check whether the receiving shader has only the older 2.x integration. |
| Color is reversed | Match Transform orientation and X/Y scale signs to the screen. |
| Light passes through walls | Enable and bake the light's shadows, or reduce its range if those surfaces should be outside it. |
| Works in Edit Mode but disappears in game | Check the Manager's Shader Stripping settings if scripts introduce Area cookies at runtime. |

## Older Shader Support

Current integrations show the image's different colors. Older **2.x shaders with Area Light support** receive one average color instead. Check [shader support](./CompatibleShaders.md) if the detail is missing.

Use [LightVolumeTVGI](./HowToUse_TVScreensIntegration.md) when you want to tint a baked bounce pattern with the screen's average color.

## Runtime Changes

Use `SetColor()` and `SetIntensity()` for tint and brightness, or `SetCustomTexture()` and `SetCustomMaterial()` to replace the Cookie. For animation, update the existing source instead of replacing it every frame. See the [UdonSharp API](./ScriptingAPI.md#pointlightvolumeinstance) for movement and snapshot options.
