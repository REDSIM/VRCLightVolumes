**VRC Light Volumes** | [How to Use](./Documentation/HowToUse.md) | [Best Practices](./Documentation/BestPractices.md) | [UdonSharp API](./Documentation/UdonSharpAPI.md) | [Unity Editor API](./Documentation/UnityEditorAPI.md) | [Shader Integration](./Documentation/ForDevelopers.md) | [Compatible Shaders](./Documentation/CompatibleShaders.md)

<p align="center"> <img src="./Documentation/LogoMain.png#gh-dark-mode-only" alt="VRC Light Volumes Main Logo" width="627" /></p>
<p align="center"> <img src="./Documentation/LogoMainBright.png#gh-light-mode-only" alt="VRC Light Volumes Main Logo" width="627" /></p>

VRC Light Volumes lights avatars, moving props and world surfaces in VRChat. Bake room lighting into **Light Volumes**, or add **Point, Spot and Area lights** that can move and change in game. Materials need a compatible shader to receive the lighting.

**[Start with the setup guide](./Documentation/HowToUse.md).**

Extending the package? Use the [UdonSharp API](./Documentation/UdonSharpAPI.md) for runtime scripts, the [Unity Editor API](./Documentation/UnityEditorAPI.md) for authoring tools and custom lightmappers, or [Shader Integration](./Documentation/ForDevelopers.md) for shaders.

This is a free and open-source asset. If it is useful to you, you can **[support the project on Patreon](https://www.patreon.com/red_sim/)**.

![](./Documentation/Preview_0.png)

## What To Use

| What you want | Start here |
|---|---|
| An avatar's face and body to pick up the room's lighting | [Regular Light Volumes](./Documentation/HowToUse_RegularLightVolumes.md) |
| A lamp, flashlight or light that changes color | [Point Light Volumes](./Documentation/HowToUse_PointLightVolumes.md) |
| A glowing screen or sign to light nearby objects | [Area Light Emission](./Documentation/HowToUse_AreaLightEmission.md) |
| Walls and objects to block a Point Light Volume | [Shadows](./Documentation/HowToUse_Shadows.md) |
| Better performance with many lights | [Best Practices](./Documentation/BestPractices.md) |
| Find why a material or avatar looks wrong | [Debugging](./Documentation/HowToUse_Debugging.md) |

For a first world, bake the static room normally and place a Regular Light Volume over the area players can reach. Add Point Light Volumes where you need lights to change in game. The [setup guide](./Documentation/HowToUse.md) walks through both.

## Main Features

- Baked lighting that varies across an avatar or prop's surface
- Up to 32 active Regular and Additive Light Volumes combined
- Up to 128 active Point, Spot or Area Light Volumes visible at the same time
- Froxel Clustering to skip local lights that cannot reach a surface
- Shadows baked in the Editor, at startup, or updated in game
- Texture, Render Texture and Material projection sources
- Textured Area Light emission for screens, signs, windows and soft panels
- Specular highlights that respond to each Point Light Volume's size
- Unity Progressive, Bakery and custom editor lightmapper integration
- Runtime control through UdonSharp
- AudioLink and TV-screen integrations
- PC and Quest/Android support with suitable world shaders
- Automatic removal of unused shader features from world builds

> [!IMPORTANT]
> World and avatar materials need a [compatible shader](./Documentation/CompatibleShaders.md) to evaluate VRC Light Volumes. Unity's built-in shaders do not read this lighting data.

Light Volumes complement lightmaps and reflection probes. They shade surfaces; visible fog and light beams require a separate effect.

## VRChat Worlds To Test It

- **[Japanese Alley - VRC Light Volumes Test](https://vrchat.com/home/launch?worldId=wrld_af756ca8-30ee-41a4-b304-2207ebf79db9)**
- **[Light Volumes x AudioLink x FakeLTCGI Test](https://vrchat.com/home/launch?worldId=wrld_ba751467-ca25-4734-91b3-7e503fc171f3)**
- **[2000s Classroom](https://vrchat.com/home/launch?worldId=wrld_f6445b27-037d-4926-b51f-d79ada716b31)**
- **[Concrete Oasis](https://vrchat.com/home/launch?worldId=wrld_3641b8d9-04da-4ee4-8b06-966ca097b1a3)**

## Attribution

The optional attribution prefab tells visitors that their avatar can use VRC Light Volumes-compatible shaders. It is available at:

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

> AudioLink is optional. Install it separately if you use the [AudioLink integration](./Documentation/HowToUse_AudioLinkIntegration.md). The core package also works without the VRChat SDK in Unity's Built-in Render Pipeline.

1. In Unity, open `Window > Package Manager`.
2. Press the `[+]` button and select **Add package from git URL...**
3. Enter:

   ```text
   https://github.com/REDSIM/VRCLightVolumes.git?path=/Packages/red.sim.lightvolumes
   ```

4. Press **Add**.

The Git URL follows `main`. When testing a prerelease, install the matching release or branch so the package and documentation describe the same version.

## Install Example Scenes And Assets

1. Open `Window > Package Manager`.
2. Select **VRC Light Volumes** under **Packages: In Project**.
3. Open the **Samples** tab.
4. Import **Examples**.
5. The sample content appears under `Assets/Samples/VRC Light Volumes/[version]/Examples`.
