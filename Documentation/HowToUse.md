[VRC Light Volumes](../README.md) | **How to Use** | [Best Practices](../Documentation/BestPractices.md) | [Udon Sharp API](../Documentation/UdonSharpAPI.md) | [For Shader Developers](../Documentation/ForShaderDevelopers.md) | [Compatible Shaders](../Documentation/CompatibleShaders.md)

# How to Use

| Menu |
|--------------|
| **VRC Light Volumes System**<br />• [Light Volumes for Avatars](#Light-Volumes-for-Avatars)<br />• [Light Volumes Quick World Setup](#Light-Volumes-Quick-World-Setup)<br />• [Point Light Volumes Quick World Setup](#Point-Light-Volumes-Quick-World-Setup) |
| [Regular Light Volumes](../Documentation/HowToUse_RegularLightVolumes.md)|
| [Point Light Volumes](../Documentation/HowToUse_PointLightVolumes.md)|
| [Point Light Volume Shadows](../Documentation/HowToUse_Shadows.md)|
| [Point Light Material Sources](../Documentation/HowToUse_PointLightMaterialSources.md)|
| [Area Light Emission](../Documentation/HowToUse_AreaLightEmission.md)|
| [Audio Link Integration](../Documentation/HowToUse_AudioLinkIntegration.md)|
| [TV Screens Integration](../Documentation/HowToUse_TVScreensIntegration.md)|
| [How Light Volumes Work?](../Documentation/HowToUse_HowItWorks.md) |

## VRC Light Volumes System

![](../Documentation/Preview_1.png)

VRC Light Volumes is fast and optimized nextgen lighting solution for VRChat. It can also work without VRChat, but was mostly designed to work with VRChat SDK3 and Udon.

#### VRC Light Volumes system consists of two main parts:

[Regular **Light Volumes**](#Light-Volumes-Quick-World-Setup) is a fast and optimized solution that replaces Unity's light probes with a better per-pixel voxel based lighting. It's similar to Adaptive Probe Volumes (APV) in Unity 6, but with manual ReflectionProbe-like volumes placement and some other extra features.

[**Point Light Volumes**](#Point-Light-Volumes-Quick-World-Setup) is a fast and optimized custom lighting system that has it's own parametric Point Light, Spot Lights and Area Lights. Point Light Volumes are not voxel based, they forms the light parametrically, or based on special LUT textures (similar to IES). They can project light cookies or cubemaps and can use baked or runtime-updated shadow maps. Modern compatible shaders can also calculate individual size-aware Point Light Volume speculars. It can be up to 128 point lights visible in one scene at the same time.

## Light Volumes for Avatars

You just need to use a [shader that has VRC Light Volumes support](/Documentation/CompatibleShaders.md) and that's it. Nothing else to do!

> [!NOTE]
> Unfortunately, there is no way to make avatars cast light volumes light, it can only be integrated into worlds.

## Light Volumes Quick World Setup

![](../Documentation/Preview_3.png)

1. Right-click in the **Hierarchy** and select `Light Volume`.
   You can also do it clicking on a **Reflection Probe** object, and new **Light Volume** will inherit its bounds.
2. Rename the new **Light Volume** to match the zone you're covering.
   Each volume must have a **unique name**.
3. In the **Light Volume** component, click `Edit Bounds` and resize the volume to fit your target area - similar to setting up a **Reflection Probe**. You can also just scale it with a scaling tool.
4. With `Adaptive Resolution` enabled, adjust `Voxels Per Unit` to control voxel density.
   Keep in mind: doubling the resolution of a 3D volume increases its file size by approximately **8x**. 
   Click `Preview Voxels` to show voxel placement grid to check the density.
5. Repeat this process to create as much volumes as you need to cover all of your areas.
6. Find the **Light Volume Manager** in your scene.
   It's automatically added when you create a **Light Volume** and lists all the volumes in your scene.
7. Assign a `Weight` to each volume.
   Volumes with higher weight have higher priority in overlapping areas.
8. Select your `Baking Mode` based on the lightmapper you're using.
   **Bakery** is highly recommended due to faster baking and better quality results.
9. Bake the scene lighting with your lightmapper (Unity Progressive or Bakery).
10. For reference, check out the `Example` scene in the `Assets/Samples/VRC Light Volumes/[version]/Examples` folder to see a working setup. You need to import the Samples through the Package Manager to see this folder. This process [described here](https://github.com/REDSIM/VRCLightVolumes/tree/main?tab=readme-ov-file#install-example-scenes-and-assets).

> [!IMPORTANT]
> The number of active Light Volumes is limited to 32 per scene at a time. However, you can dynamically enable and disable volumes at runtime to work with more than 32 total, just not simultaneously.

## Point Light Volumes Quick World Setup

![](../Documentation/Preview_2.png)

1. Right-click in the **Hierarchy** and select `Point Light Volume`.
   It will add a Point Light Volume object on your scene. It's just a simple configurable light source.

2. Select your desired Point Light Volume `Type`. It can be: `Point Light`, `Spot Light`, `Area Light`

> [!IMPORTANT]
>  Point and Spot Lights are still the cheapest, but Area Lights are no longer dramatically heavier than other light types. You can use them for movable and scalable soft boxes without special fear. Just avoid excessive overlaps, and still prefer baking a regular Light Volume when the light is fully static and doesn't need runtime control.

3. In most of the cases you need to leave the `Projection` value as `Parametric` - it's the cheapest and the most useful mode. But if you want to project a light **cubemap** (Point Light), a **cookie** (Spot Light), or textured **Area Light Emission**, assign a projection source. See [Point Light Material Sources](../Documentation/HowToUse_PointLightMaterialSources.md) when the source is generated by a Material, and [Area Light Emission](../Documentation/HowToUse_AreaLightEmission.md) for TV screens, emissive panels and textured soft boxes.

4. **Point Light Volumes work differently compared to Unity’s built-in lights.** They use light attenuation that more closely resembles how light behaves in the real world.

   First, set the `Light Source Size`. This represents the physical radius of the light-emitting surface, like a light bulb for point lights, or a flashlight reflector for spotlights. Once that’s set, adjust the `Color` and `Intensity`.

   Note that `Intensity` can be very high (in the hundreds or even thousands) for small `Light Source Size` values. This is because intensity here represents the light emitted per unit of surface area. A smaller light source must emit more intense light to achieve a reasonable visible range.

> [!TIP]
> Scaling the light game object also scales the light source size!

5. `Debug Range` shows the range in which point light volume affects meshes. Try not to overlap a lot of point light volumes. More overlaps means less performance.
   You can configure the `Brightness Cutoff` value in the **Light Volume Setup** to limit the effective range of the light and improve performance. Higher values reduce the light's visible radius, which generally increases performance, but results in less realistic light attenuation.

6. Enable `Dynamic` if your light can move in runtime. Otherwise it will be static, which is a tiny bit cheaper.
   If you want to make `Dynamic` lights auto-update their positions and other parameters in runtime, enable `Auto Update Volumes` in **Light Volume Setup**. Otherwise, they will stay in one place in game.

7. If this light needs shadows, enable `Shadows`, configure the shadow source or press `Bake Shadows` in the Point Light Volume inspector. EVSM shadow maps are stored in the shared shadow texture array controlled by **Shadow Resolution**, automatic **Shadow Format**, **Shadow Bleed Reduction** and **Shadow Min Variance** in **Light Volume Setup**. See [Point Light Volume Shadows](../Documentation/HowToUse_Shadows.md) for baked shadows, realtime shadows, stability tuning and runtime script control, or [Point Light Material Sources](../Documentation/HowToUse_PointLightMaterialSources.md) if the shadow source is generated by a Material.

> [!WARNING]
> You must use materials with a [shader that has VRC Light Volumes support](/Documentation/CompatibleShaders.md) for your world surfaces and props to see Point Light Volumes! Default Unity's shader will not work!

> [!IMPORTANT]
> The number of active Point Light Volumes is limited to 128 per scene at a time. However, you can dynamically enable and disable point light volumes at runtime to work with more than 128 total, just not simultaneously.
