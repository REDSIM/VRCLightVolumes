**VRC Light Volumes** | [How to Use](./Documentation/HowToUse.md) | [Best Practices](./Documentation/BestPractices.md) | [Scripting API](./Documentation/ScriptingAPI.md) | [Shader Integration](./Documentation/ForDevelopers.md) | [Compatible Shaders](./Documentation/CompatibleShaders.md)

<p align="center"> <img src="./Documentation/LogoMain.png#gh-dark-mode-only" alt="VRC Light Volumes Main Logo" width="627" /></p>
<p align="center"> <img src="./Documentation/LogoMainBright.png#gh-light-mode-only" alt="VRC Light Volumes Main Logo" width="627" /></p>

VRC Light Volumes is an optimized voxel-based and analytic lighting solution for Unity and VRChat that complements Unity Light Probes.

**[Installation process described here](#Installation-through-VRChat-Creator-Companion)**

**[Start using VRC Light Volumes with the setup guide](./Documentation/HowToUse.md)**

This is a free and open-source asset. If it is useful to you, you can **[support the project on Patreon](https://www.patreon.com/red_sim/)**.

![](./Documentation/Preview_0.png)

## Use Cases

- Baked lighting across avatars and moving props
- Seamless baked lighting for small static objects
- Lamps, flashlights, stage lights and glowing panels
- Image and cubemap projectors
- Screen lighting and music-reactive effects

## Main Features
- Baked voxel based lighting
- Affects avatars and dynamic props
- Fast and performant
- Up to 32 light volumes visible at the same time
- Up to 128 optimized Point, Spot or Area light sources visible at the same time
- Baked shadows for realtime Point, Spot and Area lights
- Works with dynamic batching, which potentially increases performance
- Works with Bakery, Unity Progressive lightmapper and other lightmappers
- Supports light clustering, increasing performance
- Works with AudioLink and LTCGI
- Very easy and fast to setup
- Lots of shaders already support VRC Light Volumes
- It just looks beautiful!
[See the full feature list](#full-feature-list).

## VRChat Worlds To Test It

- **[Japanese Alley - VRC Light Volumes Test](https://vrchat.com/home/launch?worldId=wrld_af756ca8-30ee-41a4-b304-2207ebf79db9)**
- **[Light Volumes x AudioLink x FakeLTCGI Test](https://vrchat.com/home/launch?worldId=wrld_ba751467-ca25-4734-91b3-7e503fc171f3)**
- **[2000s Classroom](https://vrchat.com/home/launch?worldId=wrld_f6445b27-037d-4926-b51f-d79ada716b31)**
- **[Concrete Oasis](https://vrchat.com/home/launch?worldId=wrld_3641b8d9-04da-4ee4-8b06-966ca097b1a3)**

## Attribution

Use the optional attribution prefab to tell visitors about compatible avatar shaders:

```text
Packages/red.sim.lightvolumes/Attribution/
```

<p align="center"> <img src="./Packages/red.sim.lightvolumes/Attribution/LV_Logo_B.png#gh-dark-mode-only" alt="VRC Light Volumes Logo" width="400" /></p>
<p align="center"> <img src="./Packages/red.sim.lightvolumes/Attribution/LV_Logo_A.png#gh-light-mode-only" alt="VRC Light Volumes Logo" width="400" /></p>

You can use this message instead:

```text
This world supports VRC Light Volumes. Use avatar shaders with VRC Light Volumes support for an enhanced visual experience.
VRC Light Volumes by RED_SIM — GitHub: https://github.com/REDSIM/VRCLightVolumes/
```

Attribution is optional, but appreciated.

## Installation Through VRChat Creator Companion

1. Open the [RED_SIM VPM Listing](https://redsim.github.io/vpmlisting/).
2. Press **Add to VCC**.
3. Confirm the prompt and add **VRC Light Volumes** to the target project.

## Installation Through Unity Package Manager

1. In Unity, open `Window > Package Manager`.
2. Press the `[+]` button and select **Add package from git URL...**
3. Enter:

   ```text
   https://github.com/REDSIM/VRCLightVolumes.git?path=/Packages/red.sim.lightvolumes
   ```

4. Press **Add**.

The Git URL follows `main`. When testing a prerelease, install the matching release or branch so the package and documentation describe the same version.

For existing 2.x projects, see [Migrating from 2.x to 3.x](./Documentation/BestPractices.md#migrating-from-2x-to-3x).

## Install Example Scenes And Assets

1. Open `Window > Package Manager`.
2. Select **VRC Light Volumes** under **Packages: In Project**.
3. Open the **Samples** tab.
4. Import **Examples**.
5. The sample content appears under `Assets/Samples/VRC Light Volumes/[version]/Examples`.

## Full Feature List

### Baked Lighting

- Up to **32 active Regular and Additive Light Volumes** combined.
- Per-pixel lighting with adjustable voxel density and smooth blending between volumes.
- Additive volumes for separately baked lights that can move or turn on and off together.
- Color and brightness adjustments without rebaking.
- Baking with Unity Progressive, Bakery, Hikari and Glim. A [custom lightmapper API](./Documentation/CustomLightmapperIntegration.md) supports other integrations.

See [Regular Light Volumes](./Documentation/HowToUse_RegularLightVolumes.md).

### Point, Spot And Area Lights

- Up to **128 active lights** with runtime control of position, color and intensity.
- Texture, Render Texture and Material sources for cookies and projection.
- Textured Area emission for screens, signs, windows and panels.
- Individual specular highlights that respond to each light's size.
- Shadows baked in the Editor, captured at startup or updated in game.

See [Point Light Volumes](./Documentation/HowToUse_PointLightVolumes.md), [Area Light Emission](./Documentation/HowToUse_AreaLightEmission.md) and [Shadows](./Documentation/HowToUse_Shadows.md).

### Performance And Integration

- [Froxel Clustering](./Documentation/HowToUse_FroxelClustering.md) skips lights that cannot reach a surface, with optional **Shadow Culling**.
- [Shader Stripping](./Documentation/ForDevelopers.md#shader-feature-stripping) removes unused lighting features from world builds.
- [PC and Quest/Android support](./Documentation/CompatibleShaders.md#pc-and-android) with compatible world shaders.
- [AudioLink](./Documentation/HowToUse_AudioLinkIntegration.md) and [TV-screen integration](./Documentation/HowToUse_TVScreensIntegration.md).
- [Scripting API](./Documentation/ScriptingAPI.md) for runtime light control, Editor tools, atlas processing and custom lightmappers.
- [Shader Integration](./Documentation/ForDevelopers.md) through shader code or Amplify Shader Editor.
- [Debugging tools](./Documentation/HowToUse_Debugging.md): Scene view modes, voxel previews, live Inspector data and an optional PC avatar debugger.
