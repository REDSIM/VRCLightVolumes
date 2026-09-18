[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [Scripting API](./ScriptingAPI.md) | **Shader Integration** | [Compatible Shaders](./CompatibleShaders.md)

# Shader Integration

Add Light Volumes to your Built-in Render Pipeline shader with the examples below. Amplify Shader Editor examples are in `Packages/red.sim.lightvolumes/Shaders/ASE Shaders`.

- [Amplify Shader Editor](#integrating-light-volumes-with-amplify-shader-editor-ase)
- [Diffuse and lightmap integration](#light-volume-integration-through-shader-code)
- [Specular lighting](#specular-lighting)
- [Function reference](#shader-functions)
- [Custom specular BRDF](#custom-specular-brdf)
- [Shader feature stripping](#shader-feature-stripping)

For runtime scripts, see [UdonSharp API](./ScriptingAPI.md#udonsharp-api). For atlas processing and custom lightmappers, see [Unity Editor API](./UnityEditorAPI.md).

## Integrating Light Volumes with Amplify Shader Editor (ASE)

Start with [Light Volume PBR.shader](../Packages/red.sim.lightvolumes/Shaders/ASE%20Shaders/Light%20Volume%20PBR.shader). It uses `noambient` to avoid a second ambient/probe contribution; the Light Volumes samplers supply that lighting through Emission. For lightmapped objects, keep Unity's lightmap contribution and add only additive Light Volume lighting.

| Node | When to use it |
| --- | --- |
| **Light Volume** | Sample ambient color (`L0`) and directional lighting (`L1`). Feed the outputs to **Light Volume Evaluate**. |
| **Light Volume L0** | Sample only ambient color, for particles or fog where a surface normal is not useful. |
| **Light Volume Evaluate** | Turn L0/L1 into diffuse lighting using the surface normal. Multiply by the material's diffuse color. |
| **Light Volume Specular** | Approximate highlights from already sampled L0/L1. **Dominant Direction** uses one combined light direction instead of separate red, green and blue directions. |
| **Light Volume SH Specular** | Sample L0/L1 and specular together. Use for glossy surfaces that need separate Point, Spot and Area highlights, including their source sizes, cookies and shadows. |
| **Is Light Volumes** | Return `1` when Light Volumes is enabled with a supported integration version; otherwise `0`. |
| **Light Volumes Version** | Read the scene's integration version. This is not the package version. |

Enable **AdditiveOnly** in the lightmapped branch to sample additive Regular Volumes and Point Light Volumes. Supply a normalized **WorldNormal**. Leave **WorldPositionOffset** at zero to sample the surface's actual position. See [Normals and position offset](#normals-and-position-offset) for **PointLightShading** and other input details.

Add specular directly to the final lighting without multiplying by albedo again. Use the simpler nodes on matte materials: SH Specular takes more work as more Point Light Volumes overlap.

## Light Volume integration through shader code

Use shader target 3.5 or newer for projections and shadows. Add these lines to your shader program, with Unity's helpers before the package include:

```hlsl
#pragma target 3.5
#include "UnityCG.cginc"
#include "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc"
```

### Shipping a shader integration

The include above uses the installed package and the world's **Shader Stripping** settings. You can also distribute a copy of `LightVolumes.cginc` with your shader.

To distribute your shader without requiring the package, place `LightVolumes.cginc` next to it and use:

```hlsl
#include "UnityCG.cginc"
#define VRCLV_FORCE_FULL_FEATURES
#include "LightVolumes.cginc"
```

> [!IMPORTANT]
> Define `VRCLV_FORCE_FULL_FEATURES` before the first Light Volumes include in each shader program. Otherwise, the copied file still needs the package's `LightVolumesBuildConfig.cginc`. The receiving world supplies the lighting data, so the shader's project doesn't need a Manager.

Update the copy when you adopt a newer integration version. Leave the world's generated `LightVolumesBuildConfig.cginc` out of your distribution. Your own `VRCLV_DISABLE_*` macros still apply with `VRCLV_FORCE_FULL_FEATURES`.

### Basic diffuse lighting

Put this code in your fragment function where you calculate ambient or Light Probe lighting. In a hand-written forward shader, use it only in `ForwardBase`; `ForwardAdd` would repeat it for each Unity light. Supply the fragment's world position, world normal and material albedo:

```hlsl
float3 normalWS = normalize(worldNormal);
float3 L0, L1r, L1g, L1b;
LightVolumeSH(worldPos, L0, L1r, L1g, L1b, 0, normalWS);

float3 irradiance = max(LightVolumeEvaluate(normalWS, L0, L1r, L1g, L1b), 0);
float3 diffuseLighting = irradiance * albedo;
```

Keep Unity Light Probes in the scene. In this shader branch, use `diffuseLighting` as the ambient/probe contribution, then combine it with your direct lighting and material effects. Do not add the shader's original probe result again: `LightVolumeSH()` already handles the Unity Light Probe fallback, including when Light Volumes is unavailable or too old for this include.

The fallback reads Unity's `unity_SHA*` terms, without the full L2 lighting from `ShadeSH9`. To keep your shader's original fallback, use its probe calculation when `LightVolumesEnabled() == 0`.

`LightVolumeEvaluate()` returns raw L0/L1 lighting. Clamp negative results as needed, then apply your shader's albedo, metallic diffuse reduction and ambient occlusion. The example above clamps before applying albedo.

### Additive Light Volumes for lightmapped geometry

A lightmap already contains the surface's baked lighting. Add only additive Light Volumes and Point Light Volumes to it:

```hlsl
float3 L0, L1r, L1g, L1b;
LightVolumeAdditiveSH(worldPos, L0, L1r, L1g, L1b, 0, normalWS);
float3 addedIrradiance = LightVolumeEvaluate(normalWS, L0, L1r, L1g, L1b);

// Both terms here are lighting, before multiplication by the material's diffuse color.
float3 diffuseLighting = max(decodedLightmap + addedIrradiance, 0) * albedo;
```

Use `LightVolumeSH()` for unlightmapped surfaces and `LightVolumeAdditiveSH()` for lightmapped surfaces. The additive call returns zero when Light Volumes is unavailable.

### Normals and position offset

- `worldPos` is the actual shaded world position.
- `worldPosOffset` shifts only baked Regular and additive volume sampling. Point Light Volume range, projection and shadows still use the original `worldPos`.
- `worldNormal` is a normalized world-space surface normal. Use your final normal-mapped normal when appropriate. Do not scale it to change shading strength.
- `pointLightShading` controls the additional normal-based shaping of Point Light Volumes: `0` disables that shaping, `1` gives a softer transition, and values above `1` sharpen it. The normal-aware overloads default to `3`. Use non-negative values.
- Specular calls also need `viewDir`, normalized **from the surface toward the camera**: `normalize(_WorldSpaceCameraPos.xyz - worldPos)`.

These controls affect shadows differently: `pointLightShading = 0` keeps the light's shadows, while the light's **Shading Strength = 0** disables both normal shaping and shadows.

Older overloads without `worldNormal` use `pointLightShading = 0`. Use the normal-aware calls for new surface shaders.

### Choosing where to sample

Sample per pixel for detailed volumes, cookies and shadows. Vertex sampling can help particles or foliage with heavy overdraw, but can smear lighting across large triangles. Test with your mesh on the target device.

Use L0-only sampling for fog or particles without a useful surface normal. Keep L1 on ordinary surfaces so opposite sides can receive different lighting.

## Specular lighting

| Approach | Result |
| --- | --- |
| `LightVolumeSH()` followed by `LightVolumeSpecular()` or `LightVolumeSpecularDominant()` | Approximate highlights from the combined SH lighting. Useful for an inexpensive or stylized result. Individual source shapes cannot be recovered from SH. |
| `LightVolumeSHSpecular()` | Sample once for diffuse and specular. Baked volumes use a dominant SH highlight; Point, Spot and Area lights get individual highlights with cookies, shadows and source-size broadening. |

To sample diffuse and specular together:

```hlsl
float3 L0, L1r, L1g, L1b, specular;
LightVolumeSHSpecular(worldPos, L0, L1r, L1g, L1b, specular,
    albedo, smoothness, metallic, normalWS, viewDir);

float3 irradiance = max(LightVolumeEvaluate(normalWS, L0, L1r, L1g, L1b), 0);
// Apply your shader's diffuse color/metallic/AO rules to irradiance.
// Add specular directly; it already includes the material's specular color.
```

For lightmapped surfaces, use `LightVolumeAdditiveSHSpecular()` in the additive branch. Do not also add the result of `LightVolumeSpecular()` for the same lighting.

Larger **Light Source Size** values broaden Point and Spot highlights. Area light Width and Height control their highlight size. This requires the combined SH Specular path; the SH-only specular helpers do not know each source's size.

More overlapping Point Light Volumes mean more work per pixel. **Additive Max Overdraw** limits how many contribute, including their highlights. Additive Regular Volumes have a separate counter with the same limit.

## Shader Functions

All colors are linear. `L0` is ambient RGB. `L1r`, `L1g` and `L1b` store XYZ directional coefficients for the red, green and blue channels. Their lengths also carry lighting strength; do not normalize them before evaluation.

These snippets go inside your fragment function after the include shown above. `worldPos`, `normalWS` and `viewDir` follow the [normal and position conventions](#normals-and-position-offset). Material inputs such as `albedo`, `smoothness` and `metallic` come from your shader.

### Sampling diffuse SH

`LightVolumeSH()` samples Regular, additive and Point Light Volumes. Use it for an unlightmapped surface:

```hlsl
float3 L0, L1r, L1g, L1b;
LightVolumeSH(worldPos, L0, L1r, L1g, L1b, 0, normalWS);
```

`LightVolumeAdditiveSH()` skips non-additive Regular Volumes. In a lightmapped branch, use this call with the same output variables:

```hlsl
LightVolumeAdditiveSH(worldPos, L0, L1r, L1g, L1b, 0, normalWS);
```

`LightVolumeSH_L0()` and `LightVolumeAdditiveSH_L0()` return only ambient RGB. For fog or particles without a useful surface normal, choose the full or additive result:

```hlsl
float3 particleLighting = LightVolumeSH_L0(worldPos);
float3 extraParticleLighting = LightVolumeAdditiveSH_L0(worldPos);
```

Multiply the chosen result by your particle color. Use the additive result when another source already supplies the base lighting.

Normal-aware calls take `worldPosOffset`, `worldNormal` and an optional `pointLightShading` value, which defaults to `3`. For example, `LightVolumeSH_L0(worldPos, 0, normalWS, 1)` keeps softer Point Light shaping while returning only L0.

All four functions also have overloads without `worldNormal`. These accept an optional `worldPosOffset = 0` and disable per-surface Point Light shaping. Use the normal-aware calls for new surface shaders.

### Evaluating diffuse SH

`LightVolumeEvaluate()` turns sampled SH into diffuse lighting for a surface normal. After either SH call above:

```hlsl
float3 irradiance = max(LightVolumeEvaluate(normalWS, L0, L1r, L1g, L1b), 0);
float3 diffuseLighting = irradiance * albedo;
```

The function returns the unclamped value `L0 + float3(dot(L1r, worldNormal), dot(L1g, worldNormal), dot(L1b, worldNormal))`. Use this value for a toon ramp or another lighting model. Sampled Point Light data already includes shadows; there is no separate unshadowed output.

### Specular from existing SH

`LightVolumeSpecular()` approximates highlights using separate red, green and blue SH directions. Reuse the SH values from your diffuse sample:

```hlsl
float3 specular = LightVolumeSpecular(albedo, smoothness, metallic, normalWS, viewDir,
    L0, L1r, L1g, L1b);
```

`LightVolumeSpecularDominant()` combines those directions into one simpler highlight. Use it instead of the call above:

```hlsl
float3 specular = LightVolumeSpecularDominant(albedo, smoothness, metallic, normalWS, viewDir,
    L0, L1r, L1g, L1b);
```

Add the result directly to your final lighting. For a specular-color workflow, both functions accept `f0, smoothness` in place of `albedo, smoothness, metallic`:

```hlsl
float3 specular = LightVolumeSpecular(f0, smoothness, normalWS, viewDir, L0, L1r, L1g, L1b);
```

### Sampling SH and individual specular together

`LightVolumeSHSpecular()` samples diffuse SH and individual Point, Spot and Area highlights in one call. Use it instead of separate diffuse and specular samples:

```hlsl
float3 L0, L1r, L1g, L1b, specular;
LightVolumeSHSpecular(worldPos, L0, L1r, L1g, L1b, specular,
    albedo, smoothness, metallic, normalWS, viewDir);
```

`LightVolumeAdditiveSHSpecular()` omits non-additive Regular Volumes. For a lightmapped surface, use the same outputs with:

```hlsl
LightVolumeAdditiveSHSpecular(worldPos, L0, L1r, L1g, L1b, specular,
    albedo, smoothness, metallic, normalWS, viewDir);
```

Evaluate `L0`/`L1` for diffuse lighting and add `specular` directly, as in [Specular lighting](#specular-lighting). Both functions also accept `f0, smoothness` in place of `albedo, smoothness, metallic`. Optional trailing arguments are `worldPosOffset = 0` and `pointLightShading = 3`.

For every specular helper, `smoothness` and `metallic` use `0..1`. `f0` is the linear reflection color when viewed straight on. The albedo/metallic overloads calculate `f0 = lerp(0.04, albedo, metallic)` for you.

### Availability and version

`LightVolumesEnabled()` returns `1` for enabled Light Volumes with integration version 2 or newer. Use it when you want to keep your shader's existing `probeLighting` fallback:

```hlsl
float3 irradiance = probeLighting;
if (LightVolumesEnabled() > 0) {
    float3 L0, L1r, L1g, L1b;
    LightVolumeSH(worldPos, L0, L1r, L1g, L1b, 0, normalWS);
    irradiance = max(LightVolumeEvaluate(normalWS, L0, L1r, L1g, L1b), 0);
}
```

Full sampling calls already supply their own Light Probe fallback. The guard is only needed to preserve a different fallback calculation.

`LightVolumesVersion()` reports the scene's integration version, not the package version. For a custom integration that distinguishes version-3 data:

```hlsl
bool usesVersion3Data = LightVolumesVersion() >= 3;
```

The result is `0` when absent, `1` for legacy data, or the supplied version such as `2` or `3`. Use `LightVolumesEnabled()` for availability; version-1 scenes fall back to Unity Light Probes with this include.

### Separating regular, additive and Point Light data

`LV_LightVolumeRegularSH()` samples only non-additive Regular Volumes. For a separate toon ramp, collect that group into zeroed accumulators:

```hlsl
float3 L0 = 0, L1r = 0, L1g = 0, L1b = 0;
float3 worldPosOffset = 0;
if (LightVolumesEnabled() > 0) {
    LV_LightVolumeRegularSH(worldPos + worldPosOffset, L0, L1r, L1g, L1b);
}
```

Use your own Light Probe fallback when the guard is false. To isolate another group, replace the call inside that guard:

| Function | Purpose | Call inside the guard |
| --- | --- | --- |
| `LV_LightVolumeAdditiveSH()` | Sample only additive baked volumes. | `LV_LightVolumeAdditiveSH(worldPos + worldPosOffset, L0, L1r, L1g, L1b);` |
| `LV_PointLightVolumeSH()` | Sample only Point, Spot and Area lights, including cookies and shadows. | `LV_PointLightVolumeSH(worldPos, normalWS, 3, L0, L1r, L1g, L1b);` |

Each helper adds to its `inout` values. Use separate zeroed accumulators for groups that need different ramps. Apply `worldPosOffset` only to the two baked-volume calls.

These `LV_*` helpers are internal and can change between releases. Pin and test the package version when using them. Prefer the public functions when you don't need separate groups.

## Custom Specular BRDF

`LV_SpecularBRDFDirection_Custom()` supplies your own Point, Spot and Area highlights when `LV_CUSTOM_SPECULAR_BRDF` is defined. Diffuse lighting and baked-volume SH highlights keep their existing behavior.

Define your function **before** including `LightVolumes.cginc`, then enable the hook. This example makes a hard-edged stylized highlight:

```hlsl
#include "UnityCG.cginc"

float3 LV_SpecularBRDFDirection_Custom(float3 f0, float roughness, float roughnessSq, float NoV,
    float3 worldNormal, float3 viewDir, float3 l0, float3 lightDirNormal, float lightSpreadSq) {
    float3 halfVector = lightDirNormal + viewDir;
    halfVector *= rsqrt(max(dot(halfVector, halfVector), 0.000001));
    float highlight = step(0.95, saturate(dot(worldNormal, halfVector)));
    float front = saturate(dot(worldNormal, lightDirNormal));
    return l0 * f0 * highlight * front;
}

#define LV_CUSTOM_SPECULAR_BRDF
#include "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc"
```

In your fragment function, use the combined sampler as usual; it calls your hook for each contributing light:

```hlsl
float3 L0, L1r, L1g, L1b, specular;
LightVolumeSHSpecular(worldPos, L0, L1r, L1g, L1b, specular,
    albedo, smoothness, metallic, normalWS, viewDir);
```

Use `LightVolumeAdditiveSHSpecular()` for the lightmapped branch. Supply normalized normal and view directions. The hook receives a normalized light direction. `l0` includes color, attenuation, cookie, shadow and normal-based masking. Return that light's final specular contribution; the caller adds it to the output.

`roughness` is squared perceptual roughness; `roughnessSq` is its square. `NoV` is the clamped normal/view dot product. `lightSpreadSq` describes source spread for size-aware highlights. The simple example ignores roughness and source size; use them if your model needs those effects.

Keep shared material calculations outside the hook, which runs for each contributing light. Define it in source; no `shader_feature` or `multi_compile` keyword is needed.

## Shader feature stripping

**Shader Stripping** removes unused Light Volumes code for Play Mode and world builds. Edit Mode keeps all features. **Auto** checks the primary Manager's scene, including inactive and zero-intensity lights. It can't predict script changes or find features in other scenes and external prefabs.

If scripts add a feature later, turn **Auto** off and keep that feature enabled. Switching Point to Spot needs **Spot Lights**; adding a Spot cookie needs **Spot Cookies**; rotating a non-dynamic baked volume needs **Volume Rotation**. Keep parent features too, such as **Shadows** for any shadow-map type. Disable **Shader Stripping** to keep everything while testing.

The package writes `LightVolumesBuildConfig.cginc`; leave it unchanged. Udon can't restore code removed from a built shader. Play Mode Inspector changes can recompile shaders, so test runtime changes in a world build too. Projects with the VRChat Avatars SDK keep all features, even with both SDKs installed.

Shader authors can exclude features before the include. For example, this shader keeps plain Area lights but omits their textures:

```hlsl
#define VRCLV_DISABLE_AREA_COOKIES
#include "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc"
```

Use these macros for other exclusions:

| Feature | Exclusion macro |
| --- | --- |
| Regular / additive voxel volumes | `VRCLV_DISABLE_REGULAR_VOLUMES` / `VRCLV_DISABLE_ADDITIVE_VOLUMES` |
| Point / Spot / Area lights | `VRCLV_DISABLE_POINT_LIGHTS` / `VRCLV_DISABLE_SPOT_LIGHTS` / `VRCLV_DISABLE_AREA_LIGHTS` |
| LUT falloff | `VRCLV_DISABLE_LIGHT_LUTS` |
| Point / Spot / Area cookies | `VRCLV_DISABLE_POINT_COOKIES` / `VRCLV_DISABLE_SPOT_COOKIES` / `VRCLV_DISABLE_AREA_COOKIES` |
| All shadows | `VRCLV_DISABLE_SHADOWS` |
| Cubemap / single-slice shadow maps | `VRCLV_DISABLE_CUBEMAP_SHADOWS` / `VRCLV_DISABLE_SINGLE_SLICE_SHADOWS` |
| Shadows fixed in world space | `VRCLV_DISABLE_WORLD_SPACE_SHADOWS` |
| Froxel Clustering | `VRCLV_DISABLE_CLUSTERING` |
| Rotation of baked directional lighting | `VRCLV_DISABLE_VOLUME_ROTATION` |
| Blending with Unity Light Probes | `VRCLV_DISABLE_LIGHT_PROBES_BLENDING` |
| Smooth outer volume boundaries | `VRCLV_DISABLE_SMOOTH_BOUNDS` |

Disabling a parent also removes its dependent features. Removing **Smooth Bounds** does not remove blending between overlapping volumes.

Define `VRCLV_FORCE_FULL_FEATURES` before the include only when this particular shader must ignore the scene-generated exclusions. Explicit `VRCLV_DISABLE_*` macros in your shader still apply. These macros are compile-time choices, not runtime switches or material keywords.

Clustering requires shader target 3.5 or newer on D3D11, GLCore, Vulkan, GLES3 or Metal. Public sampling calls choose the appropriate light loop. `VRCLV_DISABLE_CLUSTERING` disables clustering for this shader, regardless of the Manager setting. Leave the internal `VRCLV_CLUSTERING_SUPPORTED` macro to the include.
