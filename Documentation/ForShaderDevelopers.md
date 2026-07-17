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
