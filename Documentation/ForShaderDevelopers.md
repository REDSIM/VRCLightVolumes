[VRC Light Volumes](../README.md) | [How to Use](../Documentation/HowToUse.md) | [Best Practices](../Documentation/BestPractices.md) | [Udon Sharp API](../Documentation/UdonSharpAPI.md) | **For Shader Developers** | [Compatible Shaders](../Documentation/CompatibleShaders.md)

# For shader developers

| Menu |
| --- |
|**For Shader Developers**<br />• [Integrating Light Volumes with Amplify Shader Editor (ASE)](#Integrating-Light-Volumes-with-Amplify-Shader-Editor-(ASE))<br />• [Light Volume integration through shader code](#Light-Volume-integration-through-shader-code)<br />• [Shader Functions](#Shader-Functions)|

If you are a shader developer, it should be easy to integrate Light Volumes support into your shader.

Both shader code way with a .cginc file and Amplify Shader Editor way with special nodes are available!

## Integrating Light Volumes with Amplify Shader Editor (ASE)

![](../Documentation/Preview_15.png)

Screenshot above shows the regular Light Volumes and Speculars integration into a PBR shader in Amplify Shader Editor.

There are few ASE nodes available for you for an easy integration. Look into `Packages/VRC Light Volumes/Shaders/ASE Shaders` folder to check the integration examples.

| ASE Node | Description |
| --- | --- |
| Light Volume | Required to get the Spherical Harmonics components. Using the output values you get from it, you can calculate the speculars for your custom lighting setup. <br/> `AdditiveOnly` flag specifies if you need to only sample additive volumes and Point Light Volumes. Useful for static lightmapped meshes. `WorldPositionOffset` offsets only regular Light Volume sampling. `WorldNormal` enables Point Light Volume normal masking. Its vector length controls normal mask hardness. |
| Light Volume L0 | Required to get the L0 spherical harmonics component, or just the overall ambient color, with no directionality. This is much lighter than the LightVolume node, and recommended to use in places where there are no directionality needed. <br/> `AdditiveOnly` flag specifies if you need to only sample additive volumes and Point Light Volumes. Useful for static lightmapped meshes. `WorldPositionOffset` offsets only regular Light Volume sampling. `WorldNormal` enables Point Light Volume normal masking. Its vector length controls normal mask hardness. |
| Light Volume Evaluate | Calculates the final color you get from the light volume in some kind of a physically realistic way. But alternatively you can implement your own "Evaluate" function to make the result matching your toon shader, for example. <br/> You should usually multiply it by your "Albedo" and add to the final color, as an emission. |
| Light Volume Specular | Calculates approximated speculars based on SH components. Can be used with Light Volumes or even with any other SH L1 values, like Unity default light probes. The result should be added to the final color, just like emission. You should NOT multiply this by albedo color! <br/> `Dominant Direction` flag specifies if you want to use a simpler and lighter way of generating speculars. Generates one color specular for the dominant light direction instead of three color speculars in a regular method. |
| Is Light Volumes | Returns `0` if there are no light volumes support on the current scene, or `1` if light volumes system is provided. |
| Light Volumes Version | Returns the light volumes version. `0` means that light volumes are not presented in the scene. `2`, `3` or any other values in future, shows the global light volumes version presented in the scene. |

## Light Volume integration through shader code

First of all, you need to include the "LightVolumes.cginc" file provided with this asset, into your shader:  `#include "LightVolumes.cginc"`. 
Also be sure that you included the "UnityCG.cginc" file **BEFORE** to support the fallback to unity's light probes:  `#include "UnityCG.cginc"`

> [!IMPORTANT]
> All the functions are recommended to use in the fragment shader. All the calculations are cheap enough, unless your shader is not drawing geometry in many transparent layers.
> If you're making a shader for transparent particles, or even foliage, you might consider integrating light volumes functions on the vertex shader instead!

### 1. Basic Light Volumes Integration

Start by replacing your light probe logic (usually where `ShadeSH9()` or `unity_SHAr` is used) with `LightVolumeSH()`

Evaluate the returned SH data using `LightVolumeEvaluate()` But you can use your own method to get the lighting color.  

Typically, the result color should be multiplied by the albedo and added to the final fragment color. You may also apply AO or other adjustments before combining it.

If your shader has a reliable world normal, pass it to `LightVolumeSH()` as the optional `worldNormal` argument. This enables Point Light Volume normal masking. Point Light Volume shadows are still included without it, but they will not be additionally shaped by the per-surface normal mask.

> [!TIP]
> `LightVolumeSH()` automatically falls back to Unity’s built-in light probes if Light Volumes are not available. No need for a manual check.

### 2. Additive Light Volumes for Lightmapped Geometry

Additive light volumes are can cast light on your static lightmapped geometry. To make it work, you need to integrate a function into your lightmapped lighting section of the shader. It's probably somewhere where you use `unity_Lightmap` variable.

Call a `LightVolumeAdditiveSH()` function there to get SH components. This function samples additive Light Volumes and Point Light Volumes, but skips regular non-additive Light Volumes. It returns zeroes if Light Volumes are not available in scene.

Then evaluate the color with `LightVolumeEvaluate()` and **add** the resulting color to your lightmap output.

> [!TIP]
>  You can also check `LightVolumesEnabled() > 0` to skip evaluation entirely when Light Volumes are not represented in the scene.

### 3. World Position Offset and Normals

`worldPosOffset` is useful when you want to sample regular Light Volumes from a slightly different position, for example to reduce artifacts on custom vertex effects. This offset only affects regular voxel Light Volume sampling. Point Light Volumes still use the original `worldPos`, because their attenuation and shadows are based on the real fragment position.

`worldNormal` is optional, but recommended for surface shaders. In `LightVolumeSH()`, `LightVolumeSH_L0()`, `LightVolumeAdditiveSH()` and `LightVolumeAdditiveSH_L0()`, Point Light Volumes use it for normal masking. The vector direction controls where the mask points, and the vector length controls mask hardness: `0` disables normal masking, `1` is the default smooth front-to-back gradient, and values above `1` make the mask sharper.

### 4. Advanced Component Sampling for Stylized Shaders

For advanced setups, such as stylized toon shaders, you can sample Light Volume components separately and decide how to combine, ramp, posterize or tint them yourself.

Use these lower-level functions when you need separate control:

| Function | Result |
| --- | --- |
| `LV_LightVolumeRegularSH()` | Regular non-additive Light Volumes only. |
| `LV_LightVolumeAdditiveSH()` | Additive Light Volumes only. |
| `LV_LightVolumePointSH()` | Point Light Volumes only, with Point Light Volume shadows already included. Pass `worldNormal` to apply standard normal masking, or pass `0` if your shader handles normal masking itself. |

This is the same sampling order used by `LightVolumeSH()`, split into separate buffers:

```hlsl
float3 regularL0 = 0, regularL1r = 0, regularL1g = 0, regularL1b = 0;
float3 additiveL0 = 0, additiveL1r = 0, additiveL1g = 0, additiveL1b = 0;
float3 pointL0 = 0, pointL1r = 0, pointL1g = 0, pointL1b = 0;

if (_UdonLightVolumeEnabled == 0 || _UdonLightVolumeVersion < VRCLV_MIN_SUPPORTED_VERSION) {
    LV_SampleLightProbeDering(regularL0, regularL1r, regularL1g, regularL1b);
} else {
    LV_LightVolumeRegularSH(worldPos + worldPosOffset, regularL0, regularL1r, regularL1g, regularL1b);
    LV_LightVolumeAdditiveSH(worldPos + worldPosOffset, additiveL0, additiveL1r, additiveL1g, additiveL1b);
    LV_LightVolumePointSH(worldPos, pointL0, pointL1r, pointL1g, pointL1b, worldNormal);
}

float3 regularLight = LightVolumeEvaluate(surfaceNormal, regularL0, regularL1r, regularL1g, regularL1b);
float3 additiveLight = LightVolumeEvaluate(surfaceNormal, additiveL0, additiveL1r, additiveL1g, additiveL1b);
float3 pointLight = LightVolumeEvaluate(surfaceNormal, pointL0, pointL1r, pointL1g, pointL1b);

// Replace this with your own toon ramps, contrast curves, color grading or masks.
float3 lightVolumes = regularLight + additiveLight + pointLight;
```

`worldPosOffset` should only be applied to regular and additive Light Volume sampling. Point Light Volumes should use the original `worldPos`, because their attenuation and shadows are based on the real fragment position.

### 5. Custom SH Evaluation Notes

If you use a custom evaluation method instead of `LightVolumeEvaluate()`, make sure you use L1 components too.

> [!WARNING]
> Using L0 only (ambient term) results in unrealistic shading and can make objects look translucent.
> You must consider L1 directions—or at least the dominant direction and its magnitude for proper shading.

### 6. Specular Lighting (Optional but Recommended)

You can enhance gloss and metal surfaces with speculars from SH data:

Use `LightVolumeSpecular()` function for colored speculars. Ideal for avatars.
Use `LightVolumeSpecularDominant()` for a single specular using dominant light direction. Better for hard surface PBR shaders.

Add the result straight to your final fragment color.

These functions already apply albedo internally **do not multiply again**. You can still apply your own specular occlusion/masking if needed.

> [!NOTE]
> For more advanced shading (e.g. anisotropic specular), implement your own model based on SH data.

## Shader Functions

There are only a few functions that are really required for the integration: 

### void LightVolumeSH()
Required to get the Spherical Harmonics components. Using the output values you get from it, you can calculate the speculars for your custom lighting setup.

Also this values are required to calculate the final light you get from the light volume.

```hlsl
void LightVolumeSH(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, float3 worldPosOffset = 0, float3 worldNormal = 0)
```
| Function argument | Description |
| --- | --- |
|`float3 worldPos` | World position of the current fragment.|
|`out float3 L0` | Outputs ambient color of the current fragment.|
|`out float3 L1r`<br/>`out float3 L1g`<br/>`out float3 L1b` | Outputs vectors that stores the Red, Green and Blue light directions and power, as a magnitude of these vectors.|
|`float3 worldPosOffset` | Optional offset applied only to regular Light Volume sampling. Point Light Volumes still use `worldPos`.|
|`float3 worldNormal` | Optional world normal used by Point Light Volumes for normal masking. Its length controls mask hardness: `0` disables it, `1` is the default smooth gradient, and values above `1` make it sharper.|

### float3 LightVolumeSH_L0()

Returns ambient color L0, without calculating L1. Cheaper then LightVolumeSH(). Should be used where directionality is not important, like particles or volumetric fog.

```hlsl
float3 LightVolumeSH_L0(float3 worldPos, float3 worldPosOffset = 0, float3 worldNormal = 0)
```

| Function argument | Description |
| --- | --- |
|`float3 worldPos` | World position of the current fragment.|
|`float3 worldPosOffset` | Optional offset applied only to regular Light Volume sampling.|
|`float3 worldNormal` | Optional world normal used by Point Light Volumes for normal masking. Its length controls mask hardness: `0` disables it, `1` is the default smooth gradient, and values above `1` make it sharper.|

### void LightVolumeAdditiveSH()
Returns Spherical Harmonics components, just as LightVolumeSH() does, but only for additive Light Volumes and Point Light Volumes. This function is much lighter than LightVolumeSH(), and useful for shaders that can be used in baked lightmaps mode.

Evaluate it and add to your lightmaps color if you want to implement the additive volumes support for the baked lightmaps.

```hlsl
void LightVolumeAdditiveSH(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, float3 worldPosOffset = 0, float3 worldNormal = 0)
```

| Function argument | Description |
| --- | --- |
|`float3 worldPos` | World position of the current fragment.|
|`out float3 L0` | Outputs ambient color of the current fragment.|
|`out float3 L1r` <br/> `out float3 L1g` <br/> `out float3 L1b` | Outputs vectors that stores the Red, Green and Blue light directions and power, as a magnitude of these vectors.|
|`float3 worldPosOffset` | Optional offset applied only to regular additive Light Volume sampling. Point Light Volumes still use `worldPos`.|
|`float3 worldNormal` | Optional world normal used by Point Light Volumes for normal masking. Its length controls mask hardness: `0` disables it, `1` is the default smooth gradient, and values above `1` make it sharper.|

### float3 LightVolumeAdditiveSH_L0()

Returns ambient color L0, without calculating L1, just as LightVolumeSH_L0() does, but only for additive Light Volumes and Point Light Volumes. This function is much lighter than LightVolumeSH_L0(), and useful for shaders that can be used in baked lightmaps mode.

Evaluate it and add to your lightmaps color if you want to implement the additive volumes support for the baked lightmaps.

```hlsl
float3 LightVolumeAdditiveSH_L0(float3 worldPos, float3 worldPosOffset = 0, float3 worldNormal = 0)
```

| Function argument | Description |
| --- | --- |
|`float3 worldPos` | World position of the current fragment. |
|`float3 worldPosOffset` | Optional offset applied only to regular additive Light Volume sampling.|
|`float3 worldNormal` | Optional world normal used by Point Light Volumes for normal masking. Its length controls mask hardness: `0` disables it, `1` is the default smooth gradient, and values above `1` make it sharper.|

### float3 LightVolumeEvaluate()

Calculates the final color you get from the light volume in some kind of a physically realistic way. But alternatively you can implement your own "Evaluate" function to make the result matching your toon shader, for example.

You should usually multiply it by your "Albedo" and add to the final color, as an emission.

```hlsl
float3 LightVolumeEvaluate(float3 worldNormal, float3 L0, float3 L1r, float3 L1g, float3 L1b)
```

| Function argument | Description |
| --- | --- |
|`float3 worldNormal` | World normal of the current fragment. Must be normalized to avoid artefacts.|
|`float3 L0` <br/> `float3 L1r` <br/> `float3 L1g` <br/> `float3 L1b` | Spherical Harmonics components you got from the LightVolumeSH() function.|

### float3 LightVolumeSpecular()
Calculates approximated speculars based on SH components. Can be used with Light Volumes or even with any other SH L1 values, like Unity default light probes. The result should be added to the final color, just like emission. You should NOT multiply this by albedo color!

Usually works much better for avatars, because can show several color speculars at the same time for each of R, G, B light directions. Slightly less performant than LightVolumeSpecularDominant()

```hlsl
float3 LightVolumeSpecular(float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b)
```

| Function argument | Description |
| --- | --- |
|`float3 albedo` | Final albedo color.|
|`float smoothness` | Final surface smoothness.|
|`float metallic` | Final surface metalness.|
|`float3 worldNormal` | World normal of the current fragment. Must be normalized to avoid artefacts.|
|`float3 viewDir` | World space camera view direction. Must be normalized.|
|`float3 L0` | Ambient color component from `LightVolumeSH()`.|
|`float3 L1r` <br/> `float3 L1g` <br/> `float3 L1b` | Red, Green and Blue light direction vectors from `LightVolumeSH()`.|

You can also provide the surface's specular color directly.

```hlsl
float3 LightVolumeSpecular(float3 specColor, float smoothness, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b)
```

| Function argument | Description |
| --- | --- |
|`float3 specColor` | Final surface specular color. |
|`float smoothness` | Final surface smoothness.|
|`float3 worldNormal` | World normal of the current fragment. Must be normalized to avoid artefacts.|
|`float3 viewDir` | World space camera view direction. Must be normalized.|
|`float3 L0` | Ambient color component from `LightVolumeSH()`.|
|`float3 L1r` <br/> `float3 L1g` <br/> `float3 L1b` | Red, Green and Blue light direction vectors from `LightVolumeSH()`.|

### float3 LightVolumeSpecularDominant()
Calculates approximated speculars based on SH components. Can be used with Light Volumes or even with any other SH L1 values, like Unity default light probes. The result should be added to the final color, just like emission. You should NOT multiply this by albedo color!

Usually works better for static PBR surfaces, because can show a one color specular for the dominant light direction. Slightly more performant than LightVolumeSpecular()

```hlsl
float3 LightVolumeSpecularDominant(float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b)
```

| Function argument | Description |
| --- | --- |
|`float3 albedo` | Final albedo color.|
|`float smoothness` | Final surface smoothness.|
|`float metallic` | Final surface metalness.|
|`float3 worldNormal` | World normal of the current fragment. Must be normalized to avoid artefacts.|
|`float3 viewDir` | World space camera view direction. Must be normalized.|
|`float3 L0` | Ambient color component from `LightVolumeSH()`.|
|`float3 L1r` <br/> `float3 L1g` <br/> `float3 L1b` | Red, Green and Blue light direction vectors from `LightVolumeSH()`.|

You can also provide the surface's specular color directly.

```hlsl
float3 LightVolumeSpecularDominant(float3 specColor, float smoothness, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b)
```

| Function argument | Description |
| --- | --- |
|`float3 specColor` | Final surface specular color.|
|`float smoothness` | Final surface smoothness.|
|`float3 worldNormal` | World normal of the current fragment. Must be normalized to avoid artefacts.|
|`float3 viewDir` | World space camera view direction. Must be normalized.|
|`float3 L0` | Ambient color component from `LightVolumeSH()`.|
|`float3 L1r` <br/> `float3 L1g` <br/> `float3 L1b` | Red, Green and Blue light direction vectors from `LightVolumeSH()`.|

### float LightVolumesEnabled()
Returns `0` if there are no light volumes support on the current scene, or `1` if light volumes system is provided.

It's not mandatory to check the light volumes support by yourself, because **LightVolumeSH()** and **LightVolumeAdditiveSH()** functions already do it and fallback to Unity Light probes instead of using the light volumes.

### float LightVolumesVersion()

Returns the light volumes version. `0` means that light volumes are not presented in the scene. `2`, `3` or any other values in future, shows the global light volumes version presented in the scene.
