**VRC Light Volumes** | [How to Use](./Documentation/HowToUse.md) | [Best Practices](./Documentation/BestPractices.md) | [Udon Sharp API](./Documentation/UdonSharpAPI.md) | [For Shader Developers](./Documentation/ForShaderDevelopers.md) | [Compatible Shaders](./Documentation/CompatibleShaders.md)

<p align="center"> <img src="./Documentation/LogoMain.png#gh-dark-mode-only" alt="VRC Light Volumes Main Logo" width="627" /></p>
<p align="center"> <img src="./Documentation/LogoMainBright.png#gh-light-mode-only" alt="VRC Light Volumes Main Logo" width="627" /></p>

VRC Light Volumes is an optimized nextgen voxel based light probes replacement and analytic lighting solution for VRChat.

**[Installation process described here](#Installation-through-VRChat-Creator-Companion)**

This is a free open-source asset, so if you like it, I would be very happy if you **[Support me on Patreon](https://www.patreon.com/red_sim/ "Support me on Patreon")**.
There is a bunch of other cool assets you will get there!

![](./Documentation/Preview_0.png)

## Use Cases
- Baked partial avatars and dynamic props lighting
- Baked seamless lightmaps for small static objects
- Fast and optimized Point, Spot and Area lights
- Fast image and cubemap projectors
- Any volumetric light effects

## Main Features
- Baked per-pixel voxel based lighting
- Affects avatars and dynamic props (shader integration required)
- Fast and performant
- Can change color in runtime
- Up to 32 light volumes visible at the same time
- Up to 128 optimized Point, Spot or Area light sources visible at the same time
- Baked and runtime-updatable shadow maps for Point, Spot or Area lights
- Texture, Render Texture and Material projection sources for Point Light Volumes
- Works with dynamic batching, which potentially increases performance
- Works with Bakery or Unity Progressive lightmapper
- Works with AudioLink and TV screens
- Very easy and fast to setup
- It just looks beautiful!

## VRChat Worlds To Test It
- **[Japanese Alley - VRC Light Volumes Test](https://vrchat.com/home/launch?worldId=wrld_af756ca8-30ee-41a4-b304-2207ebf79db9)**
- **[Light Volumes x AudioLink x FakeLTCGI Test](https://vrchat.com/home/launch?worldId=wrld_ba751467-ca25-4734-91b3-7e503fc171f3)**
- **[2000s Classroom](https://vrchat.com/home/launch?worldId=wrld_f6445b27-037d-4926-b51f-d79ada716b31)**
- **[Concrete Oasis](https://vrchat.com/home/launch?worldId=wrld_3641b8d9-04da-4ee4-8b06-966ca097b1a3)**

## Attribution

It would be greatly appreciated if you place in your VRChat world an attribution prefab provided with this package.

Look for it here: `Packages/VRC Light Volumes/Attribution/`

This will help users know they can use avatars with VRC Light Volumes compatible shaders and also learn more about the system.

<p align="center"> <img src="./Packages/red.sim.lightvolumes/Attribution/LV_Logo_B.png#gh-dark-mode-only" alt="VRC Light Volumes Logo" width="400" /></p>
<p align="center"> <img src="./Packages/red.sim.lightvolumes/Attribution/LV_Logo_A.png#gh-light-mode-only" alt="VRC Light Volumes Logo" width="400" /></p>

Alternatively, you can include a message like this:

```
This world supports VRC Light Volumes. Use avatar shaders with VRC Light Volumes support for an enhanced visual experience.
VRC Light Volumes by RED_SIM — GitHub: https://github.com/REDSIM/VRCLightVolumes/
```

You're not required to include this prefab or a message - it's entirely optional. But if you do, it helps spread the word and supports the growth of this asset in the VRChat community.

## Installation Through VRChat Creator Companion
1. Go to my VPM Listing web page: https://redsim.github.io/vpmlisting/
2. Press "Add to VCC"
3. Confirm adding in the popup dialog window
4. If it didn't work and you don't see a popup, try again!

## Installation Through Unity Package Manager
1. On the top bar in Unity click `Window > Package Manager`
2. Click the `[+]` icon in the top left of the Package Manager window
3. Select "Add package from git URL..." in the dropdown menu
4. Paste this link: `https://github.com/REDSIM/VRCLightVolumes.git?path=/Packages/red.sim.lightvolumes`
5. Press Enter on your keyboard or Click the "Add" button on the right side of the input field

## Install Example Scenes And Assets

1. After you installed the VRC Light Volumes Package, go to `Window > Package Manager`
2. With `Packages: In Project` enabled, search for `VRC Light Volumes` and select it
3. Go to `Samples` tab near the package description 
4. `Import` the **Examples** package into your project
5. Example scenes and assets will be available at `Assets/Samples/VRC Light Volumes/[version]/Examples`
