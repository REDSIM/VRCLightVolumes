[VRC Light Volumes](../README.md) | **How to Use** | [Best Practices](../Documentation/BestPractices.md) | [Udon Sharp API](../Documentation/UdonSharpAPI.md) | [For Developers](../Documentation/ForDevelopers.md) | [Compatible Shaders](../Documentation/CompatibleShaders.md)

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
| **How Light Volumes Work?**<br />- [Spherical Harmonics](#Spherical-Harmonics)<br />- [Light Data](#Light-Data)<br />- [Light Data Storage](#Light-Data-Storage)<br />- [Light Volume Evaluation](#Light-Volume-Evaluation)<br />- [Specular Evaluation](#Specular-Evaluation)<br />- [Point Light Volumes](#Point-Light-Volumes)<br />- [Point Light Volume EVSM Shadows](#Point-Light-Volume-EVSM-Shadows)<br />- [Textured Area Light Emission](#Textured-Area-Light-Emission)<br />- [Animated Cookies And Material Sources](#Animated-Cookies-And-Material-Sources) |

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

This is the fastest and simplest method of evaluating SH data. There are more advanced methods, such as Geomerics or ZH3, but they are more expensive.

## Specular Evaluation

The main high-quality specular path is `LightVolumeSHSpecular()`. It samples diffuse SH lighting and specular lighting in one call, so Point Light Volumes can be evaluated as real separate light sources instead of only as averaged SH data.

This path is more expensive, but it is more correct for glossy PBR materials. Each visible Point Light Volume gets its own specular highlight with its own direction, color, cookie, shadow mask, per-surface shading and source size. A small source gives a sharper highlight. A large source gives a broader, softer highlight. Shadowed or black lights can skip the expensive specular BRDF work.

Area Light specular is still an approximation. The diffuse/SH part uses a rectangular area-light approximation, but the specular broadening treats the Area Light more like a large spherical source with a size based on the rectangle. This is much cheaper than evaluating a true rectangular area-light reflection, and it still gives the important result: bigger Area Lights make softer highlights.

Regular and additive voxel Light Volumes do not store individual lights, only SH data, so they still use the cheaper dominant-SH specular approximation. The older helpers `LightVolumeSpecular()` and `LightVolumeSpecularDominant()` also use already accumulated SH data. They are cheaper and useful when you only need a rough glossy response, but they cannot know which exact Point Light Volume created the light.

`Additive Max Overdraw` caps how many Point Light Volumes can contribute to diffuse lighting and individual speculars per pixel. This keeps worst-case cost predictable when many dynamic lights overlap.

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

In Light Volumes 3.0, Point Light Volumes can also apply per-surface shading and shadows before their SH contribution is added.

## Point Light Volume EVSM Shadows

Point Light Volume shadows are a mix between baked shadows and realtime shadows.

The expensive part of a shadow is finding what the light can see. In normal realtime shadows, Unity renders a shadow map from the light every frame or whenever the light updates. For a Point Light or Area Light, that usually means six directions, like a cubemap. That costs draw calls, CPU work and GPU rendering work.

Point Light Volume baked shadows usually do that expensive camera rendering step ahead of time. The result is saved as a shadow texture. `Bake In Game` runs the same kind of bake once from `Start()` in runtime, while stripping the editor preview texture from the build or asset bundle. In runtime, the shader only asks a simple question: "Is this pixel behind something in the saved shadow texture?" Because the receiving object can move and the shader checks the shadow every frame, the result behaves realtime on receivers. But because the shadow texture itself was baked, moving objects do not cast new shadows unless you rebake in runtime.

That is why baked Point Light Volume shadows are cheap compared to full realtime shadows. They do not render shadow cameras every frame. They only sample an already prepared texture and run the shadow visibility math in the material shader.

They are still more expensive than the same Point Light Volume without shadows, because every shadowed light needs shadow texture memory and extra shader work. Full realtime mode through **Point Light Shadow Runtime Baker** is usually heavier than Unity's built-in realtime shadows, because it has to trigger runtime shadow camera renders, encode EVSM data, optionally blur it, and copy or write the result into the shared shadow texture array. It is a custom pipeline on top of the normal frame, not Unity's built-in optimized shadow path. Reserve it for heroic lights, single flashlights or other isolated lights that truly need moving casters.

EVSM means **Exponential Variance Shadow Maps**. Instead of storing only one depth value and doing a hard depth comparison, EVSM stores filtered depth moments. This makes the shadow texture much easier to blur and filter. Compared to Unity's default Built-in Render Pipeline realtime shadows, EVSM can give smoother soft shadows and wider penumbra with fewer blocky PCF-looking steps. The tradeoff is that EVSM needs more channels, more math, and careful settings such as `Shadow Min Variance` and `Shadow Bleed Reduction` to control light bleeding and mobile precision artifacts.

Point and Area lights usually use cubemap shadows, which take six texture slices. Spot Lights can use one projected shadow texture when the angle is below 180 degrees and `Force Cubemap Shadows` is disabled, so Spot Light shadows are usually much cheaper in memory and about six times cheaper to rebake in realtime. Keep realtime Spot Light angles around 120 degrees or lower when possible for better quality.

## Textured Area Light Emission

Area Lights can also use a Cookie source as a textured emitter. The Cookie is packed into the shared Point Light Volume texture array, and the array gets mipmaps when at least one Area Light cookie is present.

The shader samples the local Cookie detail close to the Area Light and blends toward coarser mip levels as the receiver gets farther away or sees the emitter at a grazing angle. This keeps nearby high-frequency texture detail while still letting bright areas influence darker parts of the projection through the averaged mip levels.

For old shaders that do not support Area Light cookies, the manager reads the final mip level from the packed texture array and uses it as an average-color fallback for that Area Light.

## Animated Cookies And Material Sources

Animated cookies are not a special lighting simulation. Under the hood, they are texture copies.

Point Light Volume cookies, cubemaps, LUTs, Area Light cookies and shadow sources are packed into shared `Texture2DArray` render textures. Static sources are copied when the array is initialized or rebuilt. Animated RenderTexture and Material sources are copied again when `Auto Update Textures` is enabled.

For a single-slice source, such as a Spot Light cookie or Area Light cookie, the manager blits one texture or Material pass into one array slice. For a cubemap source, it writes six slices, one for each face. A Material source simply renders pass `0` into the target slice, with `_CustomRenderTextureInfo` telling the shader which slice or cubemap face is being rendered.

This keeps the shader side simple: receivers just sample the shared texture array. The cost is paid when the source is blitted, so animated cookies should use the lowest acceptable `Cookie Resolution`, and `Auto Update Textures` should stay disabled for sources that do not actually change.
