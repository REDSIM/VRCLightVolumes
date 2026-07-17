[VRC Light Volumes](../README.md) | [How to Use](../Documentation/HowToUse.md) | [Best Practices](../Documentation/BestPractices.md) | [Udon Sharp API](../Documentation/UdonSharpAPI.md) | **For Developers** | [Compatible Shaders](../Documentation/CompatibleShaders.md)

# For Developers

| Menu |
| --- |
| **Shader Integration**<br />• [Integrating Light Volumes with Amplify Shader Editor (ASE)](#integrating-light-volumes-with-amplify-shader-editor-ase)<br />• [Light Volume integration through shader code](#light-volume-integration-through-shader-code)<br />• [Shader Functions](#shader-functions) |
| **Custom Lightmapper Integration**<br />• [Overview](#custom-lightmapper-integration)<br />• [Recommended workflow](#recommended-workflow)<br />• [Editor API](#editor-api)<br />• [SH data layout](#sh-data-layout)<br />• [Validity, dilation and denoise](#validity-dilation-and-denoise)<br />• [Saving and atlas finalization](#saving-and-atlas-finalization)<br />• [Threading and failure handling](#threading-and-failure-handling) |

This page documents integration points intended for shader developers and editor tool developers.

Shader integration is available through the provided `.cginc` file or dedicated Amplify Shader Editor nodes.

## Integrating Light Volumes with Amplify Shader Editor (ASE)

![](../Documentation/Preview_15.png)

Screenshot above shows the regular Light Volumes and Speculars integration into a PBR shader in Amplify Shader Editor.

There are few ASE nodes available for you for an easy integration. Look into `Packages/red.sim.lightvolumes/Shaders/ASE Shaders` folder to check the integration examples.

| ASE Node | Description |
| --- | --- |
| Light Volume | Required to get the Spherical Harmonics components. Using the output values you get from it, you can calculate the speculars for your custom lighting setup. <br/> `AdditiveOnly` flag specifies if you need to only sample additive volumes and Point Light Volumes. Useful for static lightmapped meshes. `WorldPositionOffset` offsets voxel Light Volume sampling, but Point Light Volumes still use the real fragment position. `WorldNormal` provides the normalized surface direction for Point Light Volume shading when `PointLightShading` is greater than `0`. `PointLightShading` controls its strength and hardness; `0` disables it. |
| Light Volume L0 | Required to get the L0 spherical harmonics component, or just the overall ambient color, with no directionality. This is much lighter than the LightVolume node, and recommended to use in places where there are no directionality needed. <br/> `AdditiveOnly` flag specifies if you need to only sample additive volumes and Point Light Volumes. Useful for static lightmapped meshes. `WorldPositionOffset` offsets voxel Light Volume sampling, but Point Light Volumes still use the real fragment position. `WorldNormal` provides the normalized surface direction for Point Light Volume shading when `PointLightShading` is greater than `0`. `PointLightShading` controls its strength and hardness; `0` disables it. |
| Light Volume Evaluate | Calculates the final color you get from the light volume in some kind of a physically realistic way. But alternatively you can implement your own "Evaluate" function to make the result matching your toon shader, for example. <br/> You should usually multiply it by your "Albedo" and add to the final color, as an emission. |
| Light Volume Specular | Calculates approximated speculars based on SH components. Can be used with Light Volumes or even with any other SH L1 values, like Unity default light probes. The result should be added to the final color, just like emission. You should NOT multiply this by albedo color! <br/> `Dominant Direction` flag specifies if you want to use a simpler and lighter way of generating speculars. Generates one color specular for the dominant light direction instead of three color speculars in a regular method. |
| Light Volume SH Specular | Samples SH components and specular lighting in one node. This is the recommended PBR path when you want correct individual Point Light Volume speculars. Regular voxel Light Volumes still use dominant SH specular, while Point Light Volumes are accumulated individually with shadows, cookies, per-surface shading and source-size broadening. |
| Is Light Volumes | Returns `0` if there are no light volumes support on the current scene, or `1` if light volumes system is provided. |
| Light Volumes Version | Returns the light volumes version. `0` means that light volumes are not presented in the scene. `2`, `3` or any other values in future, shows the global light volumes version presented in the scene. |

`Light Volume SH Specular` is more expensive than the SH-only nodes in areas where several Point Light Volumes overlap, because the shader evaluates the Point Light Volume specular BRDF per visible light up to `Additive Max Overdraw`. Use it for glossy PBR surfaces where individual point light highlights matter. For matte, toon, particle or volumetric surfaces, use the SH-only or L0-only nodes when they are visually enough.

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

If your shader has a reliable normalized world normal, use the extended `LightVolumeSH()` overload and pass `worldPosOffset`, `worldNormal` and optionally `pointLightShading`. This provides the surface direction for Point Light Volume shading. Set `pointLightShading` to `0` if you do not want Point Light Volumes to be shaped by the surface normal.

> [!TIP]
> `LightVolumeSH()` automatically falls back to Unity’s built-in light probes if Light Volumes are not available. No need for a manual check.

### 2. Additive Light Volumes for Lightmapped Geometry

Additive light volumes are can cast light on your static lightmapped geometry. To make it work, you need to integrate a function into your lightmapped lighting section of the shader. It's probably somewhere where you use `unity_Lightmap` variable.

Call a `LightVolumeAdditiveSH()` function there to get SH components. This function samples additive Light Volumes and Point Light Volumes, but skips regular non-additive Light Volumes. It returns zeroes if Light Volumes are not available in scene.

Then evaluate the color with `LightVolumeEvaluate()` and **add** the resulting color to your lightmap output.

> [!TIP]
>  You can also check `LightVolumesEnabled() > 0` to skip evaluation entirely when Light Volumes are not represented in the scene.

### 3. World Position Offset, Normals and Point Light Shading

`worldPosOffset` is useful when you want to sample voxel Light Volumes from a slightly different position, for example to reduce artifacts on custom vertex effects. This offset affects regular and additive voxel Light Volume sampling. Point Light Volumes still use the original `worldPos`, because their attenuation and shadows are based on the real fragment position.

`worldNormal` is required by the extended SH-only overloads and by all specular functions. When `pointLightShading` is greater than `0`, `worldNormal` is used as-is and must already be valid and normalized. The legacy v2-compatible SH overloads do not accept a normal and explicitly disable Point Light per-surface shading. `worldNormal` always means the surface normal direction and should not be scaled to control Point Light Volume shading anymore.

`viewDir` and any custom light direction values passed to lower-level functions must also be normalized before calling Light Volumes helpers.

`pointLightShading` controls how strongly Point Light Volume contribution is shaped by `worldNormal`: `0` disables the per-surface Point Light shading, `1` gives the standard smooth front-to-back gradient, and values above `1` make the shading sharper. The extended public functions currently default this argument to `3` when it is omitted; pass `1` explicitly when you want the standard smooth profile. `worldNormal = 0` does not disable this path by itself. Negative values are not supported. The mask is source-size aware, so larger Point, Spot and Area Light Volumes fade more smoothly near the normal horizon. In `LightVolumeSHSpecular()`, the same size-aware mask also attenuates individual Point Light speculars smoothly.

### Public SH API compatibility

The current cginc keeps the v2 call shapes as explicit overloads. Calls that provide only `worldPos`, SH outputs and optional `worldPosOffset` use the legacy overload and disable per-surface Point Light shading. Calls that need v3 shading must also provide a normalized `worldNormal`. A v3 manager continues publishing the legacy globals used by v2 shaders, while a current shader uses `_UdonLightVolumeVersion` to avoid entering the v3 EVSM path under a v2 manager.

Point Light Volume shadows are already applied to the returned Point Light Volume `L0`/`L1` data. The public cginc API does not return a separate unshadowed Point Light Volume term.

### 4. Advanced Component Sampling for Stylized Shaders

For advanced setups, such as stylized toon shaders, you can sample Light Volume components separately and decide how to combine, ramp, posterize or tint them yourself.

Use these lower-level functions when you need separate control:

| Function | Result |
| --- | --- |
| `LV_LightVolumeRegularSH()` | Regular non-additive Light Volumes only. |
| `LV_LightVolumeAdditiveSH()` | Additive Light Volumes only. |
| `LV_PointLightVolumeSH()` | Point Light Volumes only, with Point Light Volume shadows already included. Inputs come first: `worldPos`, normalized `worldNormal`, `pointLightShading`, then `inout` SH accumulators. |
| `LV_PointLightVolumeSHSpecular()` | Point Light Volumes only, accumulating shadowed SH and individual specular into existing buffers. Inputs come first: `worldPos`, normalized `worldNormal`, normalized `viewDir`, `smoothness`, `f0`, `pointLightShading`, then `inout` SH/specular accumulators. |

This is the same sampling order used by `LightVolumeSH()`, split into separate buffers:

```hlsl
float3 regularL0 = 0, regularL1r = 0, regularL1g = 0, regularL1b = 0;
float3 additiveL0 = 0, additiveL1r = 0, additiveL1g = 0, additiveL1b = 0;
float3 pointL0 = 0, pointL1r = 0, pointL1g = 0, pointL1b = 0;
float pointLightShading = 1;

if (_UdonLightVolumeEnabled == 0 || _UdonLightVolumeVersion < VRCLV_MIN_SUPPORTED_VERSION) {
    LV_SampleLightProbe(regularL0, regularL1r, regularL1g, regularL1b);
} else {
    LV_LightVolumeRegularSH(worldPos + worldPosOffset, regularL0, regularL1r, regularL1g, regularL1b);
    LV_LightVolumeAdditiveSH(worldPos + worldPosOffset, additiveL0, additiveL1r, additiveL1g, additiveL1b);
    LV_PointLightVolumeSH(worldPos, worldNormal, pointLightShading, pointL0, pointL1r, pointL1g, pointL1b);
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

These functions output specular lighting directly. **Do not multiply the result by albedo again.** You can still apply your own specular occlusion/masking if needed.

For the new higher-quality path, use `LightVolumeSHSpecular()` instead of calling `LightVolumeSH()` and `LightVolumeSpecular()` separately. It samples SH components and speculars together, so Point Light Volumes can add their specular contribution per light using a GGX distribution, fast correlated Smith visibility, Schlick Fresnel, the real `NoL`, light source size roughness broadening, and size-aware Point Light shading when the source size is available. Regular Light Volumes still use dominant SH specular.

Point Light Volume size has a strong visible effect on this path. Larger `Light Source Size` values for Point and Spot Lights, and larger Width/Height values for Area Lights, produce broader and softer specular highlights. Smaller sources produce tighter highlights. Tune the light size intentionally for glossy materials instead of treating it only as a range or intensity control.

This path is intentionally more expensive than `LightVolumeSpecular()` when multiple Point Light Volumes overlap, because the Point Light Volume BRDF is evaluated per light. Fully shadowed or black Point Light Volume contribution is skipped before the BRDF work, and `Additive Max Overdraw` still caps the number of Point Light Volumes processed per pixel.

```hlsl
float3 L0, L1r, L1g, L1b, specular;
float pointLightShading = 1;
LightVolumeSHSpecular(worldPos, L0, L1r, L1g, L1b, specular, albedo, smoothness, metallic, worldNormal, viewDir, worldPosOffset, pointLightShading);

float3 diffuse = LightVolumeEvaluate(worldNormal, L0, L1r, L1g, L1b) * albedo;
finalColor += diffuse + specular;
```

For lightmapped shaders, use `LightVolumeAdditiveSHSpecular()` in the lightmap/additive lighting section and add its evaluated diffuse and specular result to the baked lighting.

> [!NOTE]
> For more advanced shading (e.g. anisotropic specular), implement your own model based on SH data.

## Shader Functions

There are only a few functions that are really required for the integration: 

### void LightVolumeSH()
Required to get the Spherical Harmonics components. Using the output values you get from it, you can calculate the speculars for your custom lighting setup.

Also this values are required to calculate the final light you get from the light volume.

```hlsl
// v2-compatible overload: Point Light per-surface shading is disabled
void LightVolumeSH(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, float3 worldPosOffset = 0)

// extended v3 overload
void LightVolumeSH(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, float3 worldPosOffset, float3 worldNormal, float pointLightShading = 3)
```
| Function argument | Description |
| --- | --- |
|`float3 worldPos` | World position of the current fragment.|
|`out float3 L0` | Outputs ambient color of the current fragment.|
|`out float3 L1r`<br/>`out float3 L1g`<br/>`out float3 L1b` | Outputs vectors that stores the Red, Green and Blue light directions and power, as a magnitude of these vectors.|
|`float3 worldPosOffset` | Optional offset applied only to regular and additive voxel Light Volume sampling. Point Light Volumes still use `worldPos`.|
|`float3 worldNormal` | Normalized world normal direction required by the extended overload and used by Point Light Volumes for per-surface shading.|
|`float pointLightShading` | Optional non-negative Point Light Volume shading strength in the extended overload. `0` disables per-surface Point Light shading, `1` gives the standard smooth gradient, values above `1` make it sharper, and the omitted-argument default is `3`.|

### float3 LightVolumeSH_L0()

Returns ambient color L0, without calculating L1. Cheaper then LightVolumeSH(). Should be used where directionality is not important, like particles or volumetric fog.

```hlsl
// v2-compatible overload
float3 LightVolumeSH_L0(float3 worldPos, float3 worldPosOffset = 0)

// extended v3 overload
float3 LightVolumeSH_L0(float3 worldPos, float3 worldPosOffset, float3 worldNormal, float pointLightShading = 3)
```

| Function argument | Description |
| --- | --- |
|`float3 worldPos` | World position of the current fragment.|
|`float3 worldPosOffset` | Optional offset applied only to regular and additive voxel Light Volume sampling. Point Light Volumes still use `worldPos`.|
|`float3 worldNormal` | Normalized world normal direction required by the extended overload and used by Point Light Volumes for per-surface shading.|
|`float pointLightShading` | Optional non-negative Point Light Volume shading strength in the extended overload. `0` disables it, `1` gives the standard smooth gradient, and the omitted-argument default is `3`.|

### void LightVolumeAdditiveSH()
Returns Spherical Harmonics components, just as LightVolumeSH() does, but only for additive Light Volumes and Point Light Volumes. This function is much lighter than LightVolumeSH(), and useful for shaders that can be used in baked lightmaps mode.

Evaluate it and add to your lightmaps color if you want to implement the additive volumes support for the baked lightmaps.

```hlsl
// v2-compatible overload
void LightVolumeAdditiveSH(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, float3 worldPosOffset = 0)

// extended v3 overload
void LightVolumeAdditiveSH(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, float3 worldPosOffset, float3 worldNormal, float pointLightShading = 3)
```

| Function argument | Description |
| --- | --- |
|`float3 worldPos` | World position of the current fragment.|
|`out float3 L0` | Outputs ambient color of the current fragment.|
|`out float3 L1r` <br/> `out float3 L1g` <br/> `out float3 L1b` | Outputs vectors that stores the Red, Green and Blue light directions and power, as a magnitude of these vectors.|
|`float3 worldPosOffset` | Optional offset applied only to additive voxel Light Volume sampling. Point Light Volumes still use `worldPos`.|
|`float3 worldNormal` | Normalized world normal direction required by the extended overload and used by Point Light Volumes for per-surface shading.|
|`float pointLightShading` | Optional non-negative Point Light Volume shading strength in the extended overload. `0` disables it, `1` gives the standard smooth gradient, and the omitted-argument default is `3`.|

### float3 LightVolumeAdditiveSH_L0()

Returns ambient color L0, without calculating L1, just as LightVolumeSH_L0() does, but only for additive Light Volumes and Point Light Volumes. This function is much lighter than LightVolumeSH_L0(), and useful for shaders that can be used in baked lightmaps mode.

Evaluate it and add to your lightmaps color if you want to implement the additive volumes support for the baked lightmaps.

```hlsl
// v2-compatible overload
float3 LightVolumeAdditiveSH_L0(float3 worldPos, float3 worldPosOffset = 0)

// extended v3 overload
float3 LightVolumeAdditiveSH_L0(float3 worldPos, float3 worldPosOffset, float3 worldNormal, float pointLightShading = 3)
```

| Function argument | Description |
| --- | --- |
|`float3 worldPos` | World position of the current fragment. |
|`float3 worldPosOffset` | Optional offset applied only to additive voxel Light Volume sampling. Point Light Volumes still use `worldPos`.|
|`float3 worldNormal` | Normalized world normal direction required by the extended overload and used by Point Light Volumes for per-surface shading.|
|`float pointLightShading` | Optional non-negative Point Light Volume shading strength in the extended overload. `0` disables it, `1` gives the standard smooth gradient, and the omitted-argument default is `3`.|

### void LightVolumeSHSpecular()

Returns Spherical Harmonics components and specular lighting in one call. This is the recommended PBR path when you want more correct speculars from Point Light Volumes. Regular Light Volumes and light probes use dominant SH specular, while Point Light Volumes are accumulated individually with a GGX/Smith/Schlick specular BRDF.

Individual Point Light Volume speculars use the light's physical source size. This makes large sources noticeably wider and softer in reflections, and makes small sources sharper.

The `L0`/`L1` outputs include the same shadowed Point Light Volume diffuse contribution that `LightVolumeSH()` would return. The `specular` output contains dominant SH specular for regular/additive voxel Light Volumes plus individual shadowed Point Light Volume speculars.

`LightVolumeSHSpecular()` falls back to Unity light probes if Light Volumes are not available, just like `LightVolumeSH()`.

```hlsl
void LightVolumeSHSpecular(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, out float3 specular, float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir, float3 worldPosOffset = 0, float pointLightShading = 3)
```

| Function argument | Description |
| --- | --- |
|`float3 worldPos` | World position of the current fragment.|
|`out float3 L0` | Outputs ambient color of the current fragment.|
|`out float3 L1r` <br/> `out float3 L1g` <br/> `out float3 L1b` | Outputs vectors that store the Red, Green and Blue light directions and power, as a magnitude of these vectors.|
|`out float3 specular` | Outputs the specular lighting. Add it directly to the final color. Do not multiply it by albedo again.|
|`float3 albedo` | Final albedo color.|
|`float smoothness` | Final surface smoothness.|
|`float metallic` | Final surface metalness.|
|`float3 worldNormal` | Normalized world normal of the current fragment. Used for specular BRDF shading and as the direction for Point Light Volume per-surface shading.|
|`float3 viewDir` | Normalized world space camera view direction.|
|`float3 worldPosOffset` | Optional offset applied only to regular and additive voxel Light Volume sampling. Point Light Volumes still use `worldPos`.|
|`float pointLightShading` | Optional non-negative Point Light Volume shading strength. `0` disables per-surface shading, `1` gives the standard smooth gradient, values above `1` make it sharper, and the omitted-argument default is `3`. Individual speculars use the same size-aware mask and keep Point Light shadows/cookies.|

You can also provide the surface's specular F0 directly.

```hlsl
void LightVolumeSHSpecular(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, out float3 specular, float3 f0, float smoothness, float3 worldNormal, float3 viewDir, float3 worldPosOffset = 0, float pointLightShading = 3)
```

### void LightVolumeAdditiveSHSpecular()

Returns additive Spherical Harmonics components and specular lighting in one call. Use this in lightmapped shaders where you only want additive Light Volumes and Point Light Volumes on top of baked lighting. Point Light Volume speculars use the same individual size-aware BRDF as `LightVolumeSHSpecular()`.

This function returns zeroes if Light Volumes are not available in scene, just like `LightVolumeAdditiveSH()`.

The `L0`/`L1` outputs include shadowed Point Light Volume diffuse contribution. The `specular` output contains dominant SH specular for additive voxel Light Volumes plus individual shadowed Point Light Volume speculars.

```hlsl
void LightVolumeAdditiveSHSpecular(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, out float3 specular, float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir, float3 worldPosOffset = 0, float pointLightShading = 3)
```

| Function argument | Description |
| --- | --- |
|`float3 worldPos` | World position of the current fragment.|
|`out float3 L0` | Outputs additive ambient color of the current fragment.|
|`out float3 L1r` <br/> `out float3 L1g` <br/> `out float3 L1b` | Outputs additive Red, Green and Blue light direction vectors.|
|`out float3 specular` | Outputs additive specular lighting. Add it directly to the final color or baked lighting result.|
|`float3 albedo` | Final albedo color.|
|`float smoothness` | Final surface smoothness.|
|`float metallic` | Final surface metalness.|
|`float3 worldNormal` | Normalized world normal of the current fragment. Used for specular BRDF shading and as the direction for Point Light Volume per-surface shading.|
|`float3 viewDir` | Normalized world space camera view direction.|
|`float3 worldPosOffset` | Optional offset applied only to additive voxel Light Volume sampling. Point Light Volumes still use `worldPos`.|
|`float pointLightShading` | Optional non-negative Point Light Volume shading strength. `0` disables per-surface shading, `1` gives the standard smooth gradient, values above `1` make it sharper, and the omitted-argument default is `3`. Individual speculars use the same size-aware mask and keep Point Light shadows/cookies.|

You can also provide the surface's specular F0 directly.

```hlsl
void LightVolumeAdditiveSHSpecular(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, out float3 specular, float3 f0, float smoothness, float3 worldNormal, float3 viewDir, float3 worldPosOffset = 0, float pointLightShading = 3)
```

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

This helper only sees already-accumulated SH data, so it cannot use individual Point Light Volume source size or evaluate each Point Light Volume separately. Use `LightVolumeSHSpecular()` when you need size-aware Point Light Volume speculars.

Usually works much better for avatars, because can show several color speculars at the same time for each of R, G, B light directions. Slightly less performant than LightVolumeSpecularDominant()

```hlsl
float3 LightVolumeSpecular(float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b)
```

| Function argument | Description |
| --- | --- |
|`float3 albedo` | Final albedo color.|
|`float smoothness` | Final surface smoothness.|
|`float metallic` | Final surface metalness.|
|`float3 worldNormal` | Normalized world normal of the current fragment.|
|`float3 viewDir` | Normalized world space camera view direction.|
|`float3 L0` | Ambient color component from `LightVolumeSH()`.|
|`float3 L1r` <br/> `float3 L1g` <br/> `float3 L1b` | Red, Green and Blue light direction vectors from `LightVolumeSH()`.|

You can also provide the surface's specular color directly.

```hlsl
float3 LightVolumeSpecular(float3 f0, float smoothness, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b)
```

| Function argument | Description |
| --- | --- |
|`float3 f0` | Final surface specular F0 color. |
|`float smoothness` | Final surface smoothness.|
|`float3 worldNormal` | Normalized world normal of the current fragment.|
|`float3 viewDir` | Normalized world space camera view direction.|
|`float3 L0` | Ambient color component from `LightVolumeSH()`.|
|`float3 L1r` <br/> `float3 L1g` <br/> `float3 L1b` | Red, Green and Blue light direction vectors from `LightVolumeSH()`.|

### float3 LightVolumeSpecularDominant()
Calculates approximated speculars based on SH components. Can be used with Light Volumes or even with any other SH L1 values, like Unity default light probes. The result should be added to the final color, just like emission. You should NOT multiply this by albedo color!

This helper only sees already-accumulated SH data, so it cannot use individual Point Light Volume source size or evaluate each Point Light Volume separately. Use `LightVolumeSHSpecular()` when you need size-aware Point Light Volume speculars.

Usually works better for static PBR surfaces, because can show a one color specular for the dominant light direction. Slightly more performant than LightVolumeSpecular()

```hlsl
float3 LightVolumeSpecularDominant(float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b)
```

| Function argument | Description |
| --- | --- |
|`float3 albedo` | Final albedo color.|
|`float smoothness` | Final surface smoothness.|
|`float metallic` | Final surface metalness.|
|`float3 worldNormal` | Normalized world normal of the current fragment.|
|`float3 viewDir` | Normalized world space camera view direction.|
|`float3 L0` | Ambient color component from `LightVolumeSH()`.|
|`float3 L1r` <br/> `float3 L1g` <br/> `float3 L1b` | Red, Green and Blue light direction vectors from `LightVolumeSH()`.|

You can also provide the surface's specular color directly.

```hlsl
float3 LightVolumeSpecularDominant(float3 f0, float smoothness, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b)
```

| Function argument | Description |
| --- | --- |
|`float3 f0` | Final surface specular F0 color.|
|`float smoothness` | Final surface smoothness.|
|`float3 worldNormal` | Normalized world normal of the current fragment.|
|`float3 viewDir` | Normalized world space camera view direction.|
|`float3 L0` | Ambient color component from `LightVolumeSH()`.|
|`float3 L1r` <br/> `float3 L1g` <br/> `float3 L1b` | Red, Green and Blue light direction vectors from `LightVolumeSH()`.|

### float LightVolumesEnabled()
Returns `0` if there are no light volumes support on the current scene, or `1` if light volumes system is provided.

It's not mandatory to check the light volumes support by yourself for the regular path, because **LightVolumeSH()** and **LightVolumeSHSpecular()** already fall back to Unity light probes. Additive functions such as **LightVolumeAdditiveSH()** and **LightVolumeAdditiveSHSpecular()** return zeroes when Light Volumes are not available.

### float LightVolumesVersion()

Returns the light volumes version. `0` means that light volumes are not presented in the scene. `2`, `3` or any other values in future, shows the global light volumes version presented in the scene.

## Custom Lightmapper Integration

Editor lightmappers can bake VRC Light Volumes directly without emulating Unity Progressive or creating Bakery volumes. The public editor API exposes the exact world-space positions that need lighting and accepts the resulting L0/L1 spherical harmonics data.

Keep integration code editor-only by placing it in an `Editor` folder, using an Editor-only assembly definition, or wrapping it in `#if UNITY_EDITOR`. Assemblies that use the API must reference `red.sim.LightVolumes`. The API is only compiled in the Unity Editor and rejects calls made in Play Mode.

Each `LightVolumeSetup` has its own zero-based list of available volumes. The list contains active, bake-enabled Light Volumes assigned to that setup; inactive volumes, volumes with `Bake` disabled and `EditorOnly` objects are excluded. Process every setup independently when the loaded scenes contain more than one.

> [!IMPORTANT]
> A custom probe ID is an index into the setup's current filtered volume list, not a persistent object ID. Do not add, remove, enable, disable, reorder or reassign Light Volumes between querying the count and submitting their baked data.

### Recommended workflow

1. Make sure every scene containing a Light Volume is saved. Baked textures are stored next to that scene.
2. On the Unity main thread, find or receive a reference to each `LightVolumeSetup` that should be baked.
3. Call `GetCustomProbesCount()` once for the setup.
4. For each ID, call `GetCustomProbes(id)` and bake one L0 vector and three L1 vectors for every returned position.
5. Keep the returned ID and array order unchanged while the external bake is running.
6. Submit all completed volumes together on the Unity main thread with the appropriate `SetCustomProbesBaked` overload.
7. Let VRC Light Volumes save the textures and perform the queued shadow-map and atlas finalization.

The following example shows the synchronous shape of an integration. `BakeSphericalHarmonics` represents the external lightmapper's own implementation:

```csharp
#if UNITY_EDITOR
using UnityEngine;
using VRCLightVolumes;

public static class CustomLightmapperIntegration {
    // Bakes every Light Volume currently exposed by one setup.
    public static void BakeSetup(LightVolumeSetup setup) {
        int volumeCount = setup.GetCustomProbesCount();

        for (int id = 0; id < volumeCount; id++) {
            Vector3[] positions = setup.GetCustomProbes(id);
            BakeSphericalHarmonics(positions, out Vector3[] l0, out Vector3[] l1r, out Vector3[] l1g, out Vector3[] l1b, out float[] validity);

            // Supplying validity enables dilation. The final true explicitly enables denoise.
            setup.SetCustomProbesBaked(id, l0, l1r, l1g, l1b, validity, true);
        }
    }

    // Replace this placeholder with the external lightmapper's SH bake.
    private static void BakeSphericalHarmonics(Vector3[] positions, out Vector3[] l0, out Vector3[] l1r, out Vector3[] l1g, out Vector3[] l1b, out float[] validity) {
        throw new System.NotImplementedException();
    }
}
#endif
```

If the lightmapper bakes asynchronously, collect the results without changing the scene's Light Volume setup, then marshal the complete submission back to the main thread. Submitting all volumes from one editor callback lets the delayed atlas request coalesce into a single finalization.

### Editor API

```csharp
int GetCustomProbesCount()
Vector3[] GetCustomProbes(int id)

void SetCustomProbesBaked(int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b)
void SetCustomProbesBaked(int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, bool denoise)
void SetCustomProbesBaked(int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, float[] validity)
void SetCustomProbesBaked(int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, float[] validity, bool denoise)
```

| Method | Description |
| --- | --- |
| `GetCustomProbesCount()` | Refreshes the setup's Light Volume list and returns the number of volumes currently exposed to custom lightmappers. |
| `GetCustomProbes(id)` | Recalculates adaptive resolution when enabled and returns the selected volume's voxel centers in world space. X changes fastest, followed by Y and then Z. |
| `SetCustomProbesBaked(...)` | Validates, optionally dilates and denoises, packs, saves and assigns the three baked Light Volume textures. It also updates the volume's baked rotation and queues finalization. |

Every L0, L1 and optional validity array must contain exactly as many elements as the position array returned for the same ID. All input arrays remain owned by the caller and are never modified by VRC Light Volumes.

### SH data layout

The API accepts first-order spherical harmonics in the same layout used by the Progressive integration:

| Array | Value for probe `i` |
| --- | --- |
| `l0[i]` | RGB ambient/L0 coefficients. |
| `l1r[i]` | XYZ directional coefficients for the red channel. |
| `l1g[i]` | XYZ directional coefficients for the green channel. |
| `l1b[i]` | XYZ directional coefficients for the blue channel. |

When the lightmapper produces a Unity `SphericalHarmonicsL2`, convert it as follows:

```csharp
l0[i] = new Vector3(sh[0, 0], sh[1, 0], sh[2, 0]);
l1r[i] = new Vector3(sh[0, 3], sh[0, 1], sh[0, 2]);
l1g[i] = new Vector3(sh[1, 3], sh[1, 1], sh[1, 2]);
l1b[i] = new Vector3(sh[2, 3], sh[2, 1], sh[2, 2]);
```

Only L0 and L1 are stored by Light Volumes; L2 coefficients are not submitted. Supply linear, Unity-compatible SH coefficient values. Do not pre-pack channels or multiply L1 by the internal `1.65` texture encoding coefficient—VRC Light Volumes applies that conversion during packing.

The points returned by `GetCustomProbes` are already transformed into world space. Bake lighting at those exact positions and preserve their array order; do not transform them into local volume space.

### Validity, dilation and denoise

Choose the overload according to the postprocessing required by the lightmapper:

| Overload suffix | Dilation | Denoise |
| --- | --- | --- |
| No additional argument | Disabled | Uses `LightVolumeSetup.Denoise`. |
| `bool denoise` | Disabled | Uses the supplied value. |
| `float[] validity` | Enabled | Uses `LightVolumeSetup.Denoise`. |
| `float[] validity, bool denoise` | Enabled | Uses the supplied value. |

Validity follows Unity Progressive's backface-hit convention. `validity[i]` is normally the fraction of rays from probe `i` that hit backfaces. A value lower than `DilationBackfaceBias` is considered valid; a value equal to or above the bias is considered invalid. This is not a mask where `1` means valid.

Supplying a validity array explicitly opts that submission into dilation, even if the setup's `DilateInvalidProbes` toggle is disabled. Conversely, an overload without validity cannot perform dilation even when that toggle is enabled. The toggle controls Progressive; a custom lightmapper controls dilation by choosing whether to pass validity. The dilation itself uses the setup's `DilationIterations` and `DilationBackfaceBias` values.

Each dilation iteration expands valid lighting by one voxel through a 3x3x3 neighborhood, including diagonal neighbors. Invalid voxels receive the average L0 and L1 values of their valid neighbors. Voxels without a valid neighbor remain unchanged. Multiple iterations allow newly filled voxels to propagate farther into invalid regions.

Denoise uses the same bilateral 3D algorithm and parameters as Progressive. When both options are requested, dilation runs first and denoise runs on the dilated result. Both operations process L0, L1r, L1g and L1b.

### Saving and atlas finalization

A successful submission creates the same three `RGBAHalf` Texture3D assets as Progressive under:

```text
<Scene Folder>/<Scene Name>/VRCLightVolumes/Temp/
```

The textures are assigned to the corresponding `LightVolume`, its inverse baked rotation is updated, and the objects are marked dirty. Shadow maps and the runtime Light Volume atlas are queued for the next editor update. Synchronous submissions made together are debounced into one atlas request.

At the queued finalization, atlas generation runs only when every volume required by the setup has three source textures, except volumes configured only to reserve UV space. Existing valid textures may satisfy volumes that were not rebaked. If the setup is not ready yet, a later successful submission queues another finalization check. Normally, custom integrations should not call `GenerateAtlas()` themselves after using this API.

> [!WARNING]
> The containing scene must have a valid saved path. Unsaved scenes cannot provide a destination for the generated Texture3D assets and the submission will be rejected.

### Threading and failure handling

Call `GetCustomProbesCount`, `GetCustomProbes` and every `SetCustomProbesBaked` overload from the Unity main thread. They access scene objects, transforms, `Texture3D`, `AssetDatabase` and editor callbacks, which are not safe to use from worker threads. The external lightmapper may perform its own lighting calculations on workers, but it must marshal API calls back to the main thread.

VRC Light Volumes internally parallelizes the CPU-heavy voxel work in dilation and bilateral denoise. These operations complete before `SetCustomProbesBaked` returns; texture creation and asset operations remain on the main thread. Do not invoke setters concurrently to add another layer of parallelism.

Invalid calls log a descriptive error and do not queue atlas finalization. Common failures include:

- Calling the API in Play Mode.
- Using a negative, expired or out-of-range volume ID.
- Passing a null SH array.
- Passing arrays whose lengths do not match the current volume resolution.
- Changing the volume resolution, transform or available volume list after retrieving positions.
- Submitting to a volume in an unsaved scene.

`GetCustomProbes` returns an empty array for an invalid ID. The setter methods return `void`, so integrations should validate their own result lengths before submission and treat logged API errors as a failed bake.
