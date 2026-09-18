[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [Scripting API](./ScriptingAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# TV Screens Integration (Older Workflow)

| Menu |
| --- |
| [Overview](./HowToUse.md) |
| [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) |
| [Point Light Volumes](./HowToUse_PointLightVolumes.md) |
| [Froxel Clustering](./HowToUse_FroxelClustering.md) |
| [Shadows](./HowToUse_Shadows.md) |
| [Material Sources](./HowToUse_PointLightMaterialSources.md) |
| [Area Light Emission](./HowToUse_AreaLightEmission.md) |
| [AudioLink](./HowToUse_AudioLinkIntegration.md) |
| **TV Screens (Older Workflow)**<br />• [Tint Baked Lighting With A Video](#tint-baked-lighting-with-a-video)<br />• [Practical Limits](#practical-limits)<br />• [Component Settings](#component-settings) |
| [Debugging](./HowToUse_Debugging.md) |
| [How It Works](./HowToUse_HowItWorks.md) |

**LightVolumeTVGI** tints baked lighting with a screen's average color. Use it with an additive Light Volume to keep a baked bounce and shadow pattern while its color follows the video.

For different image colors on nearby surfaces, use [Area Light Emission](./HowToUse_AreaLightEmission.md). TVGI uses one average color and doesn't create screen reflections.

![A screen tinting baked additive lighting around it.](./Preview_13.png)

## Tint Baked Lighting With A Video

1. In a separate baking scene, prepare an [additive Light Volume](./HowToUse_RegularLightVolumes.md#additive-light-volumes) around the area the screen should illuminate.
2. Bake using a **bright white emissive screen** as the light source. Remove unrelated lighting from this bake. White gives the runtime tint a neutral starting point; baking a colored image permanently colors the result.
3. Bring the baked volume into the main scene, keep **Additive** enabled, and turn **Bake** off to preserve its screen-only lighting. Leave the screen's emissive lighting out of the main scene bake so it is not added twice.
4. Check the volume with its **Color** set to white. It should add only the screen's baked lighting to the room. Connect TVGI after this test works.
5. Add **LightVolumeTVGI** to a GameObject and assign the video player's output to **Target Render Texture**. Despite the field name, a static Texture can also be used.
6. Add the additive volume to **Target Light Volumes**. Optionally add Point Light Volumes that should follow the same screen color.
7. Enter Play Mode with video playing. Adjust each target light's own **Intensity** and leave **Anti Flickering** enabled for smoother changes.

The source doesn't need mipmaps, and color updates don't need **Auto Update Volumes**.

Set unused target lists to **Size = 0**. Remove any Missing/None entries from lists you use.

## Practical Limits

Use an additive volume dedicated to the screen. Assigning the room's main override volume would recolor the room's other baked lights as well.

TVGI changes the bake's color, not its shape or shadows. Use a Dynamic Area Light for a moving screen.

Use either TVGI or AudioLink on each target. Both change its Color, so they will conflict.

## Component Settings

| Parameter | Meaning |
| --- | --- |
| **Target Render Texture** | Video output or static image to average. Source mipmaps are not required. |
| **Anti Flickering** | Smooths rapid changes between sampled colors. |
| **Target Light Volumes** | Usually one or more additive volumes containing only the screen's baked lighting. |
| **Target Point Light Volumes** | Optional Point Light Volumes that should use the same average color. |

If the light is too dim, first test the additive volume with a plain white Color to check the bake, then reconnect TVGI and adjust Intensity. If the image is moving but the light never changes, check the source assignment and target lists.
