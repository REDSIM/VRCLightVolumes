[VRC Light Volumes](../README.md) | **How to Use** | [Best Practices](./BestPractices.md) | [Scripting API](./ScriptingAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# TV Screens Integration

| Menu |
| --- |
| [Overview](./HowToUse.md) |
| [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) |
| [Point Light Volumes](./HowToUse_PointLightVolumes.md) |
| [Froxel Clustering](./HowToUse_FroxelClustering.md) |
| [Shadows](./HowToUse_Shadows.md) |
| [Material Sources](./HowToUse_PointLightMaterialSources.md) |
| [AudioLink](./HowToUse_AudioLinkIntegration.md) |
| **TV Screens**<br />• [Area Light Setup](#area-light-setup)<br />• [LTCGI Alternative](#ltcgi-alternative)<br />• [Older Workflow: LightVolumeTVGI](#older-workflow-lightvolumetvgi) |
| [Debugging](./HowToUse_Debugging.md) |
| [How It Works](./HowToUse_HowItWorks.md) |

Use an **Area Light** with the video player's **Render Texture** as its **Cookie** to light the surroundings with the screen's changing colors. For detailed screen reflections, consider [LTCGI](#ltcgi-alternative).

## Area Light Setup

1. Right-click in the Hierarchy, choose **Point Light Volume**, and set **Type → Area Light**.
2. Align the light's center and plane with the screen. Set its Transform X/Y scale to match the screen's width and height in meters, and point the blue local Z axis toward the room.
3. Assign the video player's output **Render Texture** to **Cookie**. Keep **Color** white and enable **Auto Update Textures** on the **Light Volume Manager**.
4. Play the video and adjust the light's **Intensity** after setting its physical size.
5. Enable **Shadows > Enabled** and click **Bake Shadows** so walls and furniture block the screen's light. If the emitting screen surface blocks the capture, add its Renderer to **Excluded Renderers**.

Changing video frames does not require another shadow bake. For a stationary screen and room, the Cookie can animate while the shadows stay baked. See [Shadows](./HowToUse_Shadows.md) for capture and blur settings.

If the player has no output Render Texture, a [Material source](./HowToUse_PointLightMaterialSources.md) can read its video image. It must receive the actual video texture. Copying a screen Material alone may miss texture overrides supplied by the player.

![A video screen lighting the surrounding scene.](./Preview_13.png)

The light spreads and mixes the image's colors instead of projecting a sharp picture. See [Area Light Cookies](./HowToUse_PointLightVolumes.md#area-light-cookies) for shader support and how the emission works.

## LTCGI Alternative

**LTCGI** can provide detailed screen reflections on surfaces with LTCGI shaders. Its Light Volumes integration also supplies diffuse lighting to avatars and props with shaders that support VRC Light Volumes.

| Feature | Area Light Cookie | LTCGI with Light Volumes |
| --- | --- | --- |
| **Reflections** | Simplified, blurred specular highlights. | Detailed reflections of the video on LTCGI-enabled materials. |
| **Avatar lighting** | Calculated per pixel. Older 2.x shaders with Area Light support receive only the average color. | Stored in Light Volumes, including for older Light Volume shaders. Spatial detail depends on voxel density. |
| **Setup and cost** | Direct Render Texture assignment and an optional shadow bake. Can be faster for simple screen lighting. | Requires LTCGI setup and a bake for its Light Volumes integration. Updating the volumes adds runtime work. |

Follow the [LTCGI integration instructions for VRC Light Volumes](https://ltcgi.dev/Advanced/VRC_Light_Volumes) for setup. Choose Area cookies for simple screen lighting, or LTCGI when detailed reflections matter. Compare performance in your scene.

## Older Workflow: LightVolumeTVGI

**LightVolumeTVGI** tints an additive Light Volume with the screen's average color. It keeps the baked bounce lighting and shadows, but uses one color for the whole volume and creates no screen reflections.

1. In a separate scene, bake an [additive Light Volume](./HowToUse_RegularLightVolumes.md#additive-light-volumes) using only the screen's bright white emission.
2. Bring the volume into the main scene, enable **Additive**, disable **Bake**, then click **Pack Light Volumes**. Keep the screen's emission out of the main lighting bake to avoid adding it twice.
3. Add **LightVolumeTVGI**, assign the player's output to **Target Render Texture**, and add the volume to **Target Light Volumes**.
4. Adjust the volume's **Intensity**. **Anti Flickering** smooths rapid color changes.

Keep unused target lists empty and remove Missing/None entries.
