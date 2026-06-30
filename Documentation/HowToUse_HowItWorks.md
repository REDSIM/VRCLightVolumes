[VRC Light Volumes](../README.md) | **How to Use** | [Best Practices](../Documentation/BestPractices.md) | [Udon Sharp API](../Documentation/UdonSharpAPI.md) | [For Shader Developers](../Documentation/ForShaderDevelopers.md) | [Compatible Shaders](../Documentation/CompatibleShaders.md)

# How to Use

| Menu |
|--------------|
| [VRC Light Volumes System](../Documentation/HowToUse.md) |
| [Regular Light Volumes](../Documentation/HowToUse_RegularLightVolumes.md)|
| [Point Light Volumes](../Documentation/HowToUse_PointLightVolumes.md)|
| [Point Light Volume Shadows](../Documentation/HowToUse_Shadows.md)|
| [Point Light Material Sources](../Documentation/HowToUse_PointLightMaterialSources.md)|
| [Area Light Emission](../Documentation/HowToUse_AreaLightEmission.md)|
| [Audio Link Integration](../Documentation/HowToUse_AudioLinkIntegration.md)|
| [TV Screens Integration](../Documentation/HowToUse_TVScreensIntegration.md)|
| **How Light Volumes Work?**<br />• [Spherical Harmonics](#Spherical-Harmonics)<br />• [Light Data](#Light-Data)<br />• [Light Data Storage](#Light-Data-Storage)<br />• [Light Volume Evaluation](#Light-Volume-Evaluation)<br />• [Specular Evaluation](#Specular-Evaluation)<br />• [Point Light Volumes](#Point-Light-Volumes)<br />• [Textured Area Light Emission](#Textured-Area-Light-Emission) |

## How Do Light Volumes Work?

This section is mainly for developers and curious users who want to understand how the Light Volumes system works under the hood. It's not necessary to read or learn this to use the system. Let's first look at how regular Light Volumes work!

## Spherical Harmonics

Spherical Harmonics (SH) are used to represent how light affects a point in space. In the case of Light Volumes, **L1 Spherical Harmonics** are used - a very rough approximation, but efficient to compute and sufficient for real-time rendering.

L1 Spherical Harmonics consist of:

- **L0** - Ambient color. Represents the average light color at a point in space. It's just a flat color with no directional information.
- **L1 Red** - Directional information for **Red** light. A vector representing the average direction the red light is coming from. The longer the vector, the brighter the light.
- **L1 Green** - Directional information for **Green** light.
- **L1 Blue** - Directional information for **Blue** light.

This is a simplified explanation of L1 SH, but much easier to understand than many technical descriptions you'll find elsewhere.

## Light Data

Light Volumes are 3D textures made of voxels - essentially 3D pixels, like blocks in Minecraft. Each voxel stores RGBA values, just like pixels in a 2D texture. However, in this system, each channel stores numerical data rather than actual color. Here's what a Light Volume voxel contains:

![](../Documentation/SH_01.png)

The arrows illustrate the L1 vectors for the Red, Green, and Blue channels - they represent the average incoming light direction per color. It's important to remember that SH L1 only stores the average light direction, so you can't tell how many actual lights are contributing to a point.

Each Light Volume holds light data for a 3D grid of world-space positions. The higher the resolution, the more accurately it represents lighting - just like with regular 2D textures.

![](../Documentation/SH_02.png)

## Light Data Storage

A regular 3D texture supports only 4 channels per voxel (RGBA), but SH L1 needs 12 channels. Therefore, we can't store all the data in a single texture.

So, we split the data into **three** separate 3D textures, each containing part of the SH data. Since the data is numeric, each SH vector component can be stored across the RGBA channels of these textures.

![](../Documentation/SH_03.png)

For better performance in shaders, all these textures are combined into a single 3D texture atlas, laid out next to each other. Some padding is added around each texture "island" to prevent light leaking between them.

![](../Documentation/SH_05.png)

Additionally, if you have multiple Light Volumes in a scene, their data is also combined into this atlas. The final result is a large 3D texture atlas that stores multiple SH volumes.

![](../Documentation/SH_04.png)

## Light Volume Evaluation

The process of sampling the atlas and evaluating the light happens entirely in the shader. That's why a material must support Light Volumes by including the appropriate shader code.

Besides the SH data atlas, the system also stores **3D UV (UVW)** information, which converts world space coordinates into positions in the SH atlas. For each pixel, the shader calculates the world position, then samples the SH data using interpolated values from nearby voxels.

Once the shader retrieves the L0 and L1 data, it computes the final color using a simple formula:

```glsl
FinalColor = L0 + dot(L1, WorldNormal);
```

This is the fastest and simplest method of evaluating SH data. There are more advanced methods (e.g., Geomerics or ZH3), but they are more expensive.

## Specular Evaluation

The current `LightVolumes.cginc` has two specular paths.

`LightVolumeSpecular()` and `LightVolumeSpecularDominant()` read already accumulated SH data. They are cheaper and can work with Light Volumes, Unity light probes, or any other L1 SH data, but they cannot know which individual Point Light Volume created the SH contribution.

`LightVolumeSHSpecular()` samples SH and specular lighting together. Regular and additive voxel Light Volumes still use dominant SH specular, while Point Light Volumes are evaluated individually with their own direction, shadow mask, cookie/projection color, per-surface shading and source size. This lets large sources produce wider highlights and small sources produce sharper highlights.

For performance, individual Point Light Volume speculars are only evaluated for visible Point Light Volumes up to `Additive Max Overdraw`. If a Point Light Volume is fully shadowed or contributes no light, its expensive specular BRDF work is skipped.

## Point Light Volumes

Point Light Volumes also use SH L1 to describe lighting - but they don't store it in voxels. Instead, it's computed analytically in real time using a mathematical formula.

Each light type has its own way of computing SH coefficients. For point lights, we use an **inverse square attenuation** formula, which is much closer to real-life lighting behavior. It also considers the **physical size** of the light source.

The attenuation formula is:

```math
Attenuation = \frac{1}{\text{LightSize}^2 + \text{DistanceToLight}^2}
```

The final color is calculated like this:

```math
FinalColor = \text{Attenuation} \times \text{Color} \times \text{Intensity} \times \text{LightSize}^2
```

In this formula, the light's **intensity** is multiplied by the square of its size, making it behave more like light emitted per unit surface area, rather than total emitted energy.

To cull lights at a distance, we use a distance-based mask:

```math
Mask = \text{Saturate}\left(1 - \frac{\text{DistanceToLight}^2}{\text{CutoffDistance}^2}\right)
```

The `Saturate()` function clamps the value between 0 and 1. The final light color is multiplied by this squared mask.

In Light Volumes 3.0, Point Light Volumes can also apply per-surface shading and EVSM shadow maps before their SH contribution is added. Shadow maps are stored in a shared runtime texture array as warped depth moments. Point and Area lights usually use cubemap shadows, while Spot Lights can use a cheaper single projected shadow texture when the cone angle allows it.

`Additive Max Overdraw` limits how many Point Light Volumes and additive Light Volumes can be accumulated for one pixel. In modern `LightVolumeSHSpecular()` shaders, it also limits how many individual Point Light Volume speculars can be evaluated. This keeps the worst-case shader cost predictable when many dynamic lights overlap.

### Textured Area Light Emission

Area Lights can also use a Cookie source as a textured emitter. The Cookie is packed into the shared Point Light Volume texture array, and the array gets mipmaps when at least one Area Light cookie is present.

The shader samples the local Cookie detail close to the Area Light and blends toward coarser mip levels as the receiver gets farther away or sees the emitter at a grazing angle. This keeps nearby high-frequency texture detail while still letting bright areas influence darker parts of the projection through the averaged mip levels.

For old shaders that do not support Area Light cookies, the manager reads the final mip level from the packed texture array and uses it as an average-color fallback for that Area Light.
