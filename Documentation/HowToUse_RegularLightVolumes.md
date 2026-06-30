[VRC Light Volumes](../README.md) | **How to Use** | [Best Practices](../Documentation/BestPractices.md) | [Udon Sharp API](../Documentation/UdonSharpAPI.md) | [For Shader Developers](../Documentation/ForShaderDevelopers.md) | [Compatible Shaders](../Documentation/CompatibleShaders.md)

# How to Use
| Menu |
| ------|
|[VRC Light Volumes System](../Documentation/HowToUse.md)|
|**Regular Light Volumes**<br />• [Light Volumes Placement](#Light-Volumes-Placement)<br />• [Auto Light Probes Placement](#Auto-Light-Probes-Placement)<br />• [Additive Light Volumes](#Additive-Light-Volumes)<br />• [Light Volumes Color Correction](#Light-Volumes-Color-Correction)<br />• [Light Volume Component Description](#Light-Volume-Component-Description)|
|[Point Light Volumes](../Documentation/HowToUse_PointLightVolumes.md)|
|[Point Light Volume Shadows](../Documentation/HowToUse_Shadows.md)|
|[Point Light Material Sources](../Documentation/HowToUse_PointLightMaterialSources.md)|
|[Area Light Emission](../Documentation/HowToUse_AreaLightEmission.md)|
|[Audio Link Integration](../Documentation/HowToUse_AudioLinkIntegration.md)|
|[TV Screens Integration](../Documentation/HowToUse_TVScreensIntegration.md)|
|[How Light Volumes Work?](../Documentation/HowToUse_HowItWorks.md)|

## Regular Light Volumes

![](../Documentation/Preview_3.png)

Light Volumes is a fast and optimized solution that replaces Unity's light probes with a better per-pixel voxel based lighting. It's similar to Adaptive Probe Volumes (APV) in Unity 6, but with manual ReflectionProbe-like volumes placement and some other extra features.

**Light Volumes** consist of two components in the editor: `Light Volume` and `Light Volume Instance`.

The `Light Volume` component is an editor-only script that helps you configure the light volume more easily. It is not included in the VRChat upload. Its purpose is to set up the `Light Volume Instance` Udon script in a user-friendly way. It also stores the required 3D textures, which are used to be packed into the final 3D atlas.

The `Light Volume Instance` component is a VRChat Udon script that stores all the data required by the Light Volumes system to render the volume. You generally shouldn’t modify its values manually in the editor - use the `Light Volume` script instead. However, if you’re writing game logic that changes volume parameters at runtime, you should reference the `Light Volume Instance` component, since it is the one that actually functions as the real volume in-game.

For runtime changes from Udon, prefer `Light Volume Instance` setter methods such as `SetColor()`, `SetIntensity()`, `SetDynamic()`, `SetAdditive()` and `SetSmoothBlending()` so the manager receives only the update it actually needs.

## Light Volumes Placement

**Light Volumes** should be placed to cover most of the walkable areas in your world. It's perfectly fine to leave some areas uncovered - in those cases, regular Unity Light Probes will be used as a fallback.

![](../Documentation/Preview_10.png)

If your scene is mostly lit with soft, uniform diffuse lighting, you don’t need to use very high light volumes resolution. In this case `Voxels Per Unit` value can be from `1` to `3` approximately, or even less for big open worlds.

However, if your scene contains sharp shadows or high-contrast lighting, using higher density is strongly recommended! In this case `Voxels Per Unit` value can be from `3` to `15` approximately, depending on the world size itself.

> [!WARNING]
> Always keep an eye on the *size estimation* in your Light Volume component - increasing the density can quickly make the data size extremely large.

A good practice is to place one large, low-resolution Light Volume to cover the entire world, and then add smaller, higher-density volumes in areas with sharp shadows or small, detailed light sources. Just make sure to extend the bounds slightly beyond the target area - some padding is needed to blend the volume edges smoothly. The `Smooth Blending` property in the Light Volume component controls the size of this padding.

If you already have **Reflection Probes** in your scene, you'll probably want your **Light Volumes** to match their bounds. To do this, right-click the Reflection Probe in the Hierarchy and create **Light Volume** as a child. Any volume created under any reflection probe will automatically inherit its bounds.

## Auto Light Probes Placement

Even though **Light Volumes** are designed to replace **Light Probes**, you should still include **Light Probes** in your scene to ensure proper lighting for avatars that do not support **VRC Light Volumes**.

Since Unity does not provide an easy way to automatically place **Light Probes**, doing it manually can be very time-consuming. **VRC Light Volumes** includes a built-in feature to generate **Light Probes** in just a few clicks.

Simply click the `Generate Light Probes` button in the Light Volume component. This will open a small configuration window and display a preview of the probes that will be placed in your scene. The probes will be arranged in a cuboid shape within the bounds of your Light Volume. The **Light Probe** density is usually much lower than the Light Volume density, but you can adjust it in the configuration window as needed.

Once you're happy with the settings, click the `Create Light Probe Group` button. This will create a Light Probe Group as a child of the Light Volume. You can manually edit or remove any unwanted probes, and you can also move the Light Probe Group out of the Light Volume hierarchy if you prefer.

## Additive Light Volumes

Before diving into additive volumes, here’s how **regular** light volumes work:

They use baked lighting data from the volume that contains a mesh and has the **highest weight**. When multiple volumes overlap, lighting data is **blended smoothly** between them.

#### Additive light volumes work differently

![](../Documentation/Preview_11.png)

They **add their light** on top of the regular volumes. Additive volumes can also affect **lightmapped static geometry**, making them ideal for dynamic lighting like toggleable or interactive lights, etc.

#### How to Bake an Additive Light Volume

Here will be explained how to bake a togglable light zone for your world. It's useful if you want to make a light switch for an apartment, or a light from a TV screen. If you want to make a point light, flashlight, or a projector, use a Point Light Volume instead! It's much cheaper and easier.

1. Create a **separate scene** for baking. Usually, a copy of your main scene if you want to bake light for a room.
2. **Disable or delete** all the lights that you don't want to bake into your additive light volume.
3. Add a **light volume** and all the lights that you want to bake.
4. Make sure the volume **fully contains the light's range**.
5. **Optionally:** For all of the **lightmap static** meshes, choose `Receive Global Illumination: Light Probes` to exclude them from baking lightmaps. (**Bakery** will still require a mesh that bakes lightmaps to start a bake)
6. Bake the scene.

![](../Documentation/Preview_12.png)

#### Once baking is done

1. **Copy** the baked Light Volume into your main scene.
2. **Enable** the `Additive` checkbox in the Light Volume component.
4. **Uncheck** the `Bake` flag to prevent this volume to be rebaked in future lightmapper bakes.
5. In **Light Volume Setup**, enable `Auto Update Volumes`.
6. In **Light Volume Setup** press `Pack Light Volumes` to generate the 3D atlas needed for volumes to work.
   
> [!IMPORTANT]
> If you see some sharp edges, it means that some undesirable light was baked into the volume.
> You can increase the volume size or tweak the **Color Correction** to get rid of undesirable light. Lowering `Shadows` in color correction section usually helps.

Now you should see your additive volume lighting up the scene.
If it doesn't work, make sure you’re using a [shader that has VRC Light Volumes support](/Documentation/CompatibleShaders.md) for all of your world surfaces and props.

You can always change the color and it's intensity in runtime via udon script to animate the light. Turning on and off the light volume game object also toggles the baked light.

You can control how many additive volumes affect a pixel using the `Additive Max Overdraw` parameter in the **Light Volume Setup** component.

> [!NOTE]
> `Additive Max Overdraw` parameter limits how many **additive volumes** are sampled **per pixel**, not how many can exist in the scene overall. The more additive volumes **intersect**, the higher the performance cost.

## Light Volumes Color Correction

The Light Volume component includes a simple color correction section. It's mainly useful for adjusting the brightness of your baked data, since it sometimes won’t match exactly how it appears in baked lightmaps.

Another common use case is reducing the `Shadows` brightness for baked additive Light Volumes to hide visible undesirable light at the edges.

The `Exposure` property adjusts the overall brightness of the baked data, similar to exposure in photography.

The `Shadows` and `Highlights` properties adjust the brightness of dark and bright regions, respectively. These settings are helpful for correcting underexposed or overexposed areas of the baked data.

Each time you change a value in this section, the **Light Volumes Atlas** will be automatically repacked. This process can take a few seconds, or longer if your atlas is large.

## Light Volume Component Description

| Parameter | Description |
| --- | --- |
|`Edit Bounds` | button enables a cuboid editing tool to configure the Light Volume bounds. Be sure you have **Gizmos** enabled in your viewport to see the tool handles.|
|`Preview Voxels` | button shows all the voxels to estimate the density of your Light Volume. If you have light volumes baked on your scene, voxels will be shaded in the baked color to preview the baked light.|
|`Size in VRAM` and `Size in bundle` | indicators shows an estimated size of the baked data. Both sizes are estimated, and the final size will be shown in the **Light Volume Setup** component after the data is baked.|
| **Volume Setup** | |
|`Dynamic` | Defines whether this volume can be moved in runtime. Disabling this option slightly improves performance.|
|`Additive` | Additive volumes apply their light on top of others as an overlay. Useful for movable and togglable lights. They can also project light onto static lightmapped objects if the surface shader supports it.|
|`Color` | Multiplies the volume’s color by this value.|
|`Intensity` | Brightness of the volume.|
|`Smooth Blending` | Size in meters of this Light Volume's overlapping regions for smooth blending with other volumes.|
| **Baked Data** | |
|`Texture 0` | Texture3D with baked SH data required for future atlas packing. It won't be uploaded to VRChat. (L0r, L0g, L0b, L1r.z)|
|`Texture 1` | Texture3D with baked SH data required for future atlas packing. It won't be uploaded to VRChat. (L1r.x, L1g.x, L1b.x, L1g.z)|
|`Texture 2` | Texture3D with baked SH data required for future atlas packing. It won't be uploaded to VRChat. (L1r.y, L1g.y, L1b.y, L1b.z)|
| **Color Correction** | |
| `Exposure` | Makes volume brighter or darker.|
| `Shadows` | Makes dark volume colors brighter or darker.|
| `Highlights` | Makes bright volume colors brighter or darker.|
| **Baking Setup** | |
|`Bake` | Uncheck it if you don't want to rebake this volume's textures.|
|`Reserve UV Space` | Reserves atlas space for this volume without baking new lighting data. This is useful for prefabs or runtime setups that need stable atlas UVW data but should not run a bake. Reserved voxels are written as white L0 and zero L1.|
|`Adaptive Resolution` | Automatically sets the resolution based on the Voxels Per Unit value.|
|`Voxels Per Unit` | Number of voxels used per meter, linearly. This value increases the Light Volume file size cubically.|
|`Resolution` | Manual Light Volume resolution in voxel count.|
