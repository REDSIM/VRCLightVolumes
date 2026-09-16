[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [UdonSharp API](./UdonSharpAPI.md) | [Unity Editor API](./UnityEditorAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# TV Screens Integration (Older Workflow)

**Guides:** [Overview](./HowToUse.md) · [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) · [Point Light Volumes](./HowToUse_PointLightVolumes.md) · [Froxel Clustering](./HowToUse_FroxelClustering.md) · [Shadows](./HowToUse_Shadows.md) · [Material Sources](./HowToUse_PointLightMaterialSources.md) · [Area Light Emission](./HowToUse_AreaLightEmission.md) · [AudioLink](./HowToUse_AudioLinkIntegration.md) · **TV Screens (Older Workflow)** · [Debugging](./HowToUse_Debugging.md) · [How It Works](./HowToUse_HowItWorks.md)

**LightVolumeTVGI** reads one average color from a screen image and applies it to lights. Use it to recolor a pre-baked additive Light Volume, keeping its baked bounce and shadow pattern. The screen color changes at runtime; that lighting pattern does not.

For a new screen that should cast different image colors onto nearby surfaces, use [Area Light Emission](./HowToUse_AreaLightEmission.md). TVGI keeps only one average color and does not create screen reflections.

![A screen tinting baked additive lighting around it.](./Preview_13.png)

## Tint Baked Lighting With A Video

1. In a separate baking scene, prepare an [additive Light Volume](./HowToUse_RegularLightVolumes.md#additive-light-volumes) around the area the screen should illuminate.
2. Bake using a **bright white emissive screen** as the light source. Remove unrelated lighting from this bake. White gives the runtime tint a neutral starting point; baking a colored image permanently colors the result.
3. Bring the baked volume into the main scene, keep **Additive** enabled, and turn **Bake** off to preserve its screen-only lighting. Leave the screen's emissive lighting out of the main scene bake so it is not added twice.
4. Check the volume with its **Color** set to white. It should add only the screen's baked lighting to the room. Connect TVGI after this test works.
5. Add **LightVolumeTVGI** to a GameObject and assign the video player's output to **Target Render Texture**. Despite the field name, a static Texture can also be used.
6. Add the additive volume to **Target Light Volumes**. Optionally add Point Light Volumes that should follow the same screen color.
7. Enter Play Mode with video playing. Adjust each target light's own **Intensity** and leave **Anti Flickering** enabled for smoother changes.

The source does **not** need mipmaps: TVGI makes its own small mipmapped texture to calculate the average. **Auto Update Volumes** is also unnecessary for this color update.

Keep both target lists initialized. Set an unused list's **Size** to `0` and remove any Missing/None entries from populated lists; TVGI does not skip missing targets.

## Practical Limits

Use an additive volume dedicated to the screen. Assigning the room's main override volume would recolor the room's other baked lights as well.

The receiver still needs a compatible shader. Moving props and avatars can sample the changing baked light, but TVGI does not capture new moving shadows or move the baked bounce pattern with a moving screen. Use a Dynamic Area Light when the emitter must move.

Do not add the same screen contribution twice. If the main scene already includes its baked light, adding this additive result makes it brighter again. Also avoid driving one target with both TVGI and AudioLink: both replace its Color.

## Component Settings

| Parameter | Meaning |
| --- | --- |
| **Target Render Texture** | Video output or static image to average. Source mipmaps are not required. |
| **Anti Flickering** | Smooths rapid changes between sampled colors. |
| **Target Light Volumes** | Usually one or more additive volumes containing only the screen's baked lighting. |
| **Target Point Light Volumes** | Optional Point Light Volumes that should use the same average color. |

If the light is too dim, first test the additive volume with a plain white Color to check the bake, then reconnect TVGI and adjust Intensity. If the image is moving but the light never changes, check the source assignment and target lists.
