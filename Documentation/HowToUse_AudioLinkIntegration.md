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
| **AudioLink**<br />• [Make A Lamp Pulse With The Bass](#make-a-lamp-pulse-with-the-bass)<br />• [Useful Variations](#useful-variations)<br />• [Component Settings](#component-settings) |
| [TV Screens (Older Workflow)](./HowToUse_TVScreensIntegration.md) |
| [Debugging](./HowToUse_Debugging.md) |
| [How It Works](./HowToUse_HowItWorks.md) |

Use **LightVolumeAudioLink** to make a lamp and its visible glow react to music. It can also animate baked additive lighting or follow AudioLink theme colors.

Install and set up [AudioLink](https://github.com/llealloo/audiolink/) to use this feature.

![Music-reactive light and emission.](./Preview_14.gif)

## Make A Lamp Pulse With The Bass

First confirm that the lamp lights the scene with a fixed Color and Intensity. Add AudioLink control after this test works.

1. Set up a working AudioLink component and audio source in your scene.
2. Add **LightVolumeAudioLink** to a GameObject and assign the scene's AudioLink component to **Audio Link**.
3. Add the lamp's light to **Target Point Light Volumes**.
4. Choose **Audio Band → Bass**, **Color Mode → Override Color**, and your desired **Color**.
5. Leave **Minimum Multiply** and **Maximum Multiply** at `1`, both **Add** values at `0`, and **Invert** off. The light now rises from dark with the sampled bass level.
6. Leave **Smoothing Enabled** on. Start at `0.25`; increase Smoothing if the pulses are too abrupt.
7. Enter Play Mode with audio playing. Adjust the light's own **Intensity** to set its peak brightness.

To match the visible lamp to its light, also add its renderer to **Target Mesh Renderers**. The shader must have an enabled emission path using `_EmissionColor`. Adjust **Materials Intensity** to brighten the visible fixture without changing the light output.

Color animation doesn't need **Auto Update Volumes**. Keep **Dynamic** off unless the light also moves.

## Useful Variations

| Result | Settings |
| --- | --- |
| Keep some light between beats | Set both Add values to `0.2` and both Multiply values to `0.8`. This maps a band level of 0 to 20% brightness and 1 to 100%. |
| Follow AudioLink theme colors | Use **Color Mode → Auto**. Bass, Low Mid, High Mid and Treble select theme colors 0–3. Volume uses theme color 0. |
| Dim on a beat | Enable **Invert**, keep both Multiply values at `1` and both Add values at `0`. |
| Delay a second fixture | Use another LightVolumeAudioLink with a higher **Delay** and assign that fixture only to it. |
| Animate baked bounce lighting | Assign a white-baked additive Regular Light Volume to **Target Light Volumes**. Its baked lighting pattern stays the same while its color changes. |

A nonzero **Minimum Multiply** alone does not keep the light on in silence: it still multiplies the sampled band level. Use **Minimum Add** for an idle brightness floor.

Avoid assigning the same light to multiple color-driving components. AudioLink and TVGI both replace the target's color; the last writer wins.

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
| **Target Light Volumes / Target Point Light Volumes** | Lights to recolor. Leave unused lists empty. |
| **Target Mesh Renderers** | Visible objects whose emission should follow the light. |

If a light responds but its visible mesh does not, check the material's emission setting and property names. If nothing responds, check the Audio Link reference, audio playback and Color Mode first.

For custom response curves, see the [AudioLink response formula](./TechnicalDetails.md#audiolink-response).
