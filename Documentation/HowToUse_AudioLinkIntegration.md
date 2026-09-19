[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [Scripting API](./ScriptingAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# AudioLink Integration

| Menu |
| --- |
| [Overview](./HowToUse.md) |
| [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) |
| [Point Light Volumes](./HowToUse_PointLightVolumes.md) |
| [Froxel Clustering](./HowToUse_FroxelClustering.md) |
| [Shadows](./HowToUse_Shadows.md) |
| [Material Sources](./HowToUse_PointLightMaterialSources.md) |
| **AudioLink**<br />• [Setup AudioLink](#setup-audiolink)<br />• [Component Settings](#component-settings) |
| [TV Screens](./HowToUse_TVScreensIntegration.md) |
| [Debugging](./HowToUse_Debugging.md) |
| [How It Works](./HowToUse_HowItWorks.md) |

Use **LightVolumeAudioLink** to control light color and brightness from AudioLink. It works with Point, Spot and Area lights, baked Light Volumes, and material emission.

Install and set up [AudioLink](https://github.com/llealloo/audiolink/) to use this feature.

![Music-reactive light and emission.](./Preview_14.gif)

## Setup AudioLink

With AudioLink receiving audio in your scene:

1. Add **LightVolumeAudioLink** to a GameObject and assign the scene's AudioLink component to **Audio Link**.
2. Assign Point, Spot or Area lights to **Target Point Light Volumes**, and baked volumes to **Target Light Volumes**. Leave unused lists empty.
3. Choose an **Audio Band** and **Color Mode**. **Auto** follows AudioLink theme colors; **Override Color** uses your chosen **Color**.
4. Adjust **Smoothing**, **Invert**, and the **Multiply** and **Add** settings to control how the lights respond.
5. Check the result in Play Mode with audio playing. Set each light's overall brightness with its **Intensity**.

To make visible surfaces follow the same response, add their renderers to **Target Mesh Renderers**. Their shaders must have emission enabled and use `_EmissionColor`. **Materials Intensity** controls their brightness separately from the light output.

Color animation doesn't need **Auto Update Volumes**. Keep **Dynamic** off unless the light also moves.

## Component Settings

| Parameter | Meaning |
| --- | --- |
| **Audio Link** | Scene AudioLink component to read. |
| **Audio Band** | Choose Bass, Low Mid, High Mid, Treble or Volume. |
| **Delay** | History offset `0–127`, not a time in seconds. `0` is current data. Volume ignores Delay. |
| **Smoothing Enabled / Smoothing** | Smooths brightness changes; larger values react more slowly. |
| **Invert** | Reverse the main brightness response. |
| **Minimum / Maximum Multiply** | Multiply the response at quiet and loud levels. |
| **Minimum / Maximum Add** | Add brightness at quiet and loud levels. Use Minimum Add to keep some light in silence. |
| **Color Mode** | Auto, a selected theme color, Override Color, or No Change. **No Change stops all target updates**, including brightness. |
| **Normalize Colors** | Makes sampled theme colors fully saturated and bright before the audio response. Does not modify Override Color. |
| **Color** | Color used by Override Color. It replaces the target's color rather than multiplying its original tint. |
| **Set Base Color** | Change the material's base color as well as emission. Requires a `_Color` property. |
| **Materials Intensity** | Extra multiplier for renderer output; does not change Light Volume intensity. |
| **Target Light Volumes / Target Point Light Volumes** | Lights whose color and brightness follow AudioLink. Leave unused lists empty. |
| **Target Mesh Renderers** | Visible objects whose emission should follow the light. |

If a light responds but its visible mesh does not, check the material's emission setting and property names. If nothing responds, check the Audio Link reference, audio playback and Color Mode first.
