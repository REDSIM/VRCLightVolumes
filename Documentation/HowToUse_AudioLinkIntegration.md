[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [UdonSharp API](./UdonSharpAPI.md) | [Unity Editor API](./UnityEditorAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# AudioLink Integration

**Guides:** [Overview](./HowToUse.md) · [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) · [Point Light Volumes](./HowToUse_PointLightVolumes.md) · [Froxel Clustering](./HowToUse_FroxelClustering.md) · [Shadows](./HowToUse_Shadows.md) · [Material Sources](./HowToUse_PointLightMaterialSources.md) · [Area Light Emission](./HowToUse_AreaLightEmission.md) · **AudioLink** · [TV Screens (Older Workflow)](./HowToUse_TVScreensIntegration.md) · [Debugging](./HowToUse_Debugging.md) · [How It Works](./HowToUse_HowItWorks.md)

**LightVolumeAudioLink** makes lights and emissive objects react to music. It can control Regular Light Volumes, Point Light Volumes and renderer colors together. Use it for a glowing fixture whose light pulses with the bass, or baked additive lighting that follows AudioLink theme colors.

[AudioLink](https://github.com/llealloo/audiolink/) is optional. Install and configure it only if you need this feature. VRC Light Volumes imports without it; an unassigned AudioLink reference leaves this component inactive.

![Music-reactive light and emission.](./Preview_14.gif)

## Make A Lamp Pulse With The Bass

First confirm that the lamp lights the scene with a fixed Color and Intensity. Add AudioLink control after this test works.

1. Set up a working AudioLink component and audio source in your scene.
2. Add **LightVolumeAudioLink** to a GameObject and assign the scene's AudioLink component to **Audio Link**. The integration enables AudioLink readback automatically.
3. Add the lamp's light to **Target Point Light Volumes**.
4. Choose **Audio Band → Bass**, **Color Mode → Override Color**, and your desired **Color**.
5. Leave **Minimum Multiply** and **Maximum Multiply** at `1`, both **Add** values at `0`, and **Invert** off. The light now rises from dark with the sampled bass level.
6. Leave **Smoothing Enabled** on. Start at `0.25`; increase Smoothing if the pulses are too abrupt.
7. Enter Play Mode with audio playing. Adjust the light's own **Intensity** to set its peak brightness.

To match the visible lamp to its light, also add its renderer to **Target Mesh Renderers**. The shader must have an enabled emission path using `_EmissionColor`. Adjust **Materials Intensity** to brighten the visible fixture without changing the light output.

**Auto Update Volumes is not needed for color animation.** The component uses the light setters directly. Keep Dynamic off unless the light also moves.

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
| **Audio Band** | Bass, Low Mid, High Mid, Treble or Volume. Volume reads the current RMS left-channel level. |
| **Delay** | History offset `0–127`, not a time in seconds. `0` is current data. Volume ignores Delay. |
| **Smoothing Enabled / Smoothing** | Smooths brightness changes; larger values react more slowly. |
| **Invert** | Replaces the sampled level with its inverse for the main brightness response. |
| **Minimum / Maximum Multiply** | Scale factors interpolated as the sampled level rises. |
| **Minimum / Maximum Add** | Added brightness interpolated as the sampled level rises. |
| **Color Mode** | Auto, a selected theme color, Override Color, or No Change. **No Change stops all target updates**, including brightness. |
| **Normalize Colors** | Makes sampled theme colors fully saturated and bright before the audio response. Does not modify Override Color. |
| **Color** | Color used by Override Color. It replaces the target's color rather than multiplying its original tint. |
| **Set Base Color** | Also writes `_Color` on target renderers. Leave off when only emission should change. |
| **Materials Intensity** | Extra multiplier for renderer output; does not change Light Volume intensity. |
| **Target Light Volumes / Target Point Light Volumes** | Lights to recolor. Unused lists and missing elements are skipped. |
| **Target Mesh Renderers** | Renderers to update through a Material Property Block. |

If a light responds but its visible mesh does not, check the material's emission setting and property names. If nothing responds, check the Audio Link reference, audio playback and Color Mode first.

For precise response shaping, the component uses the smoothed band level `a` as follows:

```text
response = (Invert ? 1 - a : a) × lerp(MinimumMultiply, MaximumMultiply, a)
         + lerp(MinimumAdd, MaximumAdd, a)
```

Invert changes the main response; the Multiply/Add interpolation still follows the original sampled level.
