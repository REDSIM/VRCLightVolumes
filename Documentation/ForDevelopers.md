[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [Scripting API](./ScriptingAPI.md) | **Shader Integration** | [Compatible Shaders](./CompatibleShaders.md)

# Shader Integration

| Menu |
| --- |
| **Shader Integration**<br />• [Amplify Shader Editor](#integrating-light-volumes-with-amplify-shader-editor-ase)<br />• [Shader Code Setup](#light-volume-integration-through-shader-code)<br />• [Shipping Without The Package](#shipping-without-the-package)<br />• [Directional Shading With Individual Speculars](#directional-shading-with-individual-speculars)<br />• [Directional Shading](#directional-shading)<br />• [Non-Directional Shading](#non-directional-shading)<br />• [Separate Lighting Groups](#separate-lighting-groups)<br />• [Custom Specular BRDF](#custom-specular-brdf)<br />• [Shader Stripping](#shader-feature-stripping) |
| [Shader Functions](./ShaderFunctions.md) |

## Integrating Light Volumes with Amplify Shader Editor (ASE)

The [ASE Shaders folder](../Packages/red.sim.lightvolumes/Shaders/ASE%20Shaders) contains ready-to-use shaders that also serve as integration examples. Open their ASE graphs to see how they work and build your own shaders from them.

- **Ambient Light:** disable it under **Rendering Options** for **Standard Surface**, or **Additional Options** for **Built-In/Lit**, to avoid counting probe lighting twice.
- **Normals:** always normalize the world-space normals you supply to the nodes.
- **Lightmaps:** a `LIGHTMAP_ON` **Switch** with **Mode = Fetch** selects **AdditiveOnly** outputs on **True**, and full outputs on **False**. Keep the shader's lightmap lighting enabled.
- **Output:** evaluate SH with **Light Volume Evaluate**, then multiply by diffuse color. L0 skips Evaluate. For PBR, apply metallic diffuse reduction. Add **Specular**, if used, without multiplying it by albedo, and send the result plus your own emission to **Emission**.

Keep the output node's usual material inputs connected. For directionless fog or particles using **Light Volume L0**, set **Point Light Shading** to `0`.

These nodes are available in ASE's node menu; search for **Light Volume**:

| Node | Description |
| --- | --- |
| **Light Volume SH Specular** | Diffuse SH and individual highlights for PBR. Also includes approximate baked-volume highlights. |
| **Light Volume** | Diffuse SH for directional lighting, without individual highlights. |
| **Light Volume L0** | Ambient color for fog, particles and other surfaces that do not need light direction. |
| **Light Volume Evaluate** | Converts SH to diffuse lighting using the surface normal. |
| **Light Volume Specular** | Approximate highlights from existing SH. **Dominant Direction** gives one cheaper highlight. Do not add it on top of SH Specular. |
| **Is Light Volumes** | `1` when supported Light Volumes are enabled; otherwise `0`. |
| **Light Volumes Version** | Scene integration version, not the package version. |

## Light Volume integration through shader code

Use target 3.5 or newer. Include `UnityCG.cginc` before `LightVolumes.cginc`. With the package installed:

```hlsl
#pragma target 3.5
#include "UnityCG.cginc"
#include "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc"
```

### Where To Add The Lighting

- Add the lighting in **ForwardBase**. **ForwardAdd** would repeat it for each additional Unity light, which is unnecessary.
- Use the full sample in place of the shader's probe lighting. Probe fallback is already included; keep probes in the scene.
- For lightmapped meshes, keep your shader's existing lightmap lighting and add the **Additive** result. The examples below calculate only the Light Volume contribution.
- Keep your shader's direct lights, reflections, AO and emission.

> [!IMPORTANT]
> Calculate Light Volumes in the **Fragment** stage for avatars and most solid surfaces. Prefer the **Vertex** stage for small particles, foliage and other surfaces with heavy overdraw: avoiding repeated lighting calculations for overlapping pixels can be much faster. Vertex lighting is interpolated across triangles, so it loses small lighting details and cannot use the per-pixel normal map.

## Shipping Without The Package

You can distribute Light Volumes support in two ways:

- **Package dependency:** use the package include path shown above. Your shader will not compile without the Light Volumes package installed.
- **Bundled files:** ship your own `LightVolumes.cginc` and `LightVolumesBuildConfig.cginc`. Your shader then compiles without the package.

For bundled files, keep both files in the same folder. Ship `LightVolumesBuildConfig.cginc` with only this exact first-line comment:

```hlsl
// VRC Light Volumes: managed shader stripping config
```

> [!IMPORTANT]
> The filename must be exactly `LightVolumesBuildConfig.cginc`, including capitalization. The first-line marker identifies it as a Light Volumes config that the package may overwrite. If either the filename or marker differs, the file stays untouched and does not receive **Shader Stripping** settings. Keep it writable and reset it to just the marker before distribution; do not ship generated `VRCLV_DISABLE_*` definitions from your test world.

Use the path to your copy in every Light Volumes include. If it is beside the shader:

```hlsl
#include "UnityCG.cginc"
#include "LightVolumes.cginc"
```

No extra defines are required. Without the package, all features stay available. With it installed, marked configs follow its **Shader Stripping** settings.

The receiving world supplies the lighting data, so the shader's project doesn't need a Manager. Update your copied `LightVolumes.cginc` when adopting a newer integration version.

## Directional Shading With Individual Speculars

Use this for PBR: Point, Spot and Area lights get physically based highlights with source size, cookies and shadows. Baked-volume highlights are included automatically as an SH approximation.

The Additive version includes only Additive volumes and Point/Spot/Area lights. It does not calculate highlights from Regular Volumes or the lightmap.

```hlsl
// Use your shader's world-space position and normal, with normal mapping applied.
float3 normalWS = normalize(worldNormal);
float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos); // Surface toward camera.
float3 worldPosOffset = 0; // Offset baked-volume sampling only; leave zero normally.
float pointLightShading = 3; // Normal shaping: 0 = off, 1 = softer, higher = sharper.

// All colors are linear. Smoothness and metallic are in 0..1.
float3 L0, L1r, L1g, L1b, specular;

#ifdef LIGHTMAP_ON
    // Additive volumes and Point/Spot/Area lights; the shader already handles the lightmap.
    LightVolumeAdditiveSHSpecular(worldPos, L0, L1r, L1g, L1b, specular,
        albedo, smoothness, metallic, normalWS, viewDir, worldPosOffset, pointLightShading);
#else
    // Sample all Light Volumes, with automatic probe fallback.
    LightVolumeSHSpecular(worldPos, L0, L1r, L1g, L1b, specular,
        albedo, smoothness, metallic, normalWS, viewDir, worldPosOffset, pointLightShading);
#endif

// Evaluate diffuse lighting. diffuseColor includes your PBR metallic/energy reduction.
float3 irradiance = LightVolumeEvaluate(normalWS, L0, L1r, L1g, L1b);
// Specular is already colored: add it once, without another albedo multiplication.
float3 volumeLighting = max(irradiance, 0) * diffuseColor + specular;
```

## Directional Shading

Use this cheaper path for toon shaders or materials that do not need individual highlights. For lightmapped meshes, the Additive call adds only Additive volumes and Point/Spot/Area lights.

```hlsl
// Use the shader's world-space position and final normal.
float3 normalWS = normalize(worldNormal);
float3 worldPosOffset = 0; // Shifts baked-volume samples, not Point Light sampling.
float pointLightShading = 3; // 0 disables normal shaping, but keeps shadows.
float3 L0, L1r, L1g, L1b;

#ifdef LIGHTMAP_ON
    // Sample extra lighting for lightmapped surfaces.
    LightVolumeAdditiveSH(worldPos, L0, L1r, L1g, L1b, worldPosOffset, normalWS, pointLightShading);
#else
    // Sample all Light Volumes, with automatic probe fallback.
    LightVolumeSH(worldPos, L0, L1r, L1g, L1b, worldPosOffset, normalWS, pointLightShading);
#endif

// Evaluate directionality; a toon shader can apply its ramp here.
float3 irradiance = LightVolumeEvaluate(normalWS, L0, L1r, L1g, L1b);
float3 volumeLighting = max(irradiance, 0) * diffuseColor; // Usually the material's albedo.
```

For approximate highlights from the same SH, use [LightVolumeSpecular](./ShaderFunctions.md#float3-lightvolumespecular) or [LightVolumeSpecularDominant](./ShaderFunctions.md#float3-lightvolumespeculardominant).

## Non-Directional Shading

Use L0 for fog, particles or foliage that only needs ambient color. This cheaper path needs no normal or Evaluate call. Cookies and shadows still work.

```hlsl
float3 worldPosOffset = 0; // Optional offset for baked-volume samples only.

#ifdef LIGHTMAP_ON
    // Sample extra ambient lighting for lightmapped surfaces.
    float3 irradiance = LightVolumeAdditiveSH_L0(worldPos, worldPosOffset);
#else
    // Ambient only, with automatic probe fallback.
    float3 irradiance = LightVolumeSH_L0(worldPos, worldPosOffset);
#endif

// Apply the particle or surface color to the Light Volume contribution.
float3 volumeLighting = max(irradiance, 0) * diffuseColor;
```

## Separate Lighting Groups

Use the internal `LV_*` functions together to adjust Regular, Additive and Point Light lighting separately. This example returns three diffuse-lighting results for your own tints, intensities or toon ramps.

Define the function after the Light Volumes include:

```hlsl
void SampleLightVolumeGroups(float3 worldPos, float3 normalWS,
    out float3 regular, out float3 additive, out float3 pointLights) {
    // Regular volumes, including their configured probe fallback.
    float3 L0 = 0, L1r = 0, L1g = 0, L1b = 0;
    LV_LightVolumeRegularSH(worldPos, L0, L1r, L1g, L1b);
    regular = LightVolumeEvaluate(normalWS, L0, L1r, L1g, L1b);

    // Reset the SH values before sampling the next group.
    L0 = L1r = L1g = L1b = 0;
    LV_LightVolumeAdditiveSH(worldPos, L0, L1r, L1g, L1b);
    additive = LightVolumeEvaluate(normalWS, L0, L1r, L1g, L1b);

    // Point, Spot and Area lights.
    L0 = L1r = L1g = L1b = 0;
    LV_PointLightVolumeSH(worldPos, normalWS, 3, L0, L1r, L1g, L1b);
    pointLights = LightVolumeEvaluate(normalWS, L0, L1r, L1g, L1b);
}
```

For a surface without lightmaps, keep the shader's existing `probeLighting` as fallback:

```hlsl
float3 regular = probeLighting, additive = 0, pointLights = 0;
if (LightVolumesEnabled() > 0) {
    SampleLightVolumeGroups(worldPos, normalize(worldNormal), regular, additive, pointLights);
}

// Independent intensities: Regular, Additive, then Point/Spot/Area.
float3 intensity = float3(1, 1, 1);
float3 irradiance = regular * intensity.x + additive * intensity.y + pointLights * intensity.z;
// Apply the material color after combining the groups.
float3 volumeLighting = max(irradiance, 0) * diffuseColor;
```

For lightmapped surfaces, leave out `regular` and add only `additive` and `pointLights` to the existing lightmap lighting. Keep the `LightVolumesEnabled()` check: internal helpers do not check availability themselves. Their API can change between releases.

## Custom Specular BRDF

`LV_SpecularBRDFDirection_Custom()` supplies your own Point, Spot and Area highlights when `LV_CUSTOM_SPECULAR_BRDF` is defined. Diffuse lighting and baked-volume SH highlights keep their existing behavior.

Define your function **before** including `LightVolumes.cginc`, then enable the hook. Replace the placeholder below with your own calculation:

```hlsl
#include "UnityCG.cginc"

float3 LV_SpecularBRDFDirection_Custom(float3 f0, float roughness, float roughnessSq, float NoV,
    float3 worldNormal, float3 viewDir, float3 l0, float3 lightDirNormal, float lightSpreadSq) {
    // Your code here: calculate this light's final RGB specular contribution.
    return 0; // Placeholder: no specular until replaced with your result.
}

// Enable this function before including the Light Volumes sampler.
#define LV_CUSTOM_SPECULAR_BRDF
#include "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc"
```

In your fragment function, use the combined sampler as usual; it calls your hook for each contributing light:

```hlsl
float3 L0, L1r, L1g, L1b, specular;
LightVolumeSHSpecular(worldPos, L0, L1r, L1g, L1b, specular, albedo, smoothness, metallic, normalWS, viewDir);
```

Use `LightVolumeAdditiveSHSpecular()` for the lightmapped branch. Supply normalized normal and view directions. The hook receives a normalized light direction. `l0` includes color, attenuation, cookie, shadow and normal-based masking. Return that light's final specular contribution; the caller adds it to the output.

`roughness` is squared perceptual roughness; `roughnessSq` is its square. `NoV` is the clamped normal/view dot product. `lightSpreadSq` describes source spread for size-aware highlights.

Keep shared material calculations outside the hook, which runs for each contributing light. Define it in source; no `shader_feature` or `multi_compile` keyword is needed.

## Shader feature stripping

**Shader Stripping** removes unused Light Volumes code for Play Mode and world builds. Edit Mode keeps all features. **Auto** checks the primary Manager's scene, including inactive and zero-intensity lights. It can't predict script changes or find features in other scenes and external prefabs.

If scripts add a feature later, turn **Auto** off and keep that feature enabled. Switching Point to Spot needs **Spot Lights**; adding a Spot cookie needs **Spot Cookies**; rotating a non-dynamic baked volume needs **Volume Rotation**. Keep parent features too, such as **Shadows** for any shadow-map type. Disable **Shader Stripping** to keep everything while testing.

The package updates all marked `LightVolumesBuildConfig.cginc` copies, including those [shipped with other shaders](#shipping-without-the-package). It tracks asset and package changes, and writes/imports only changed files. Udon can't restore code removed from a built shader. Play Mode Inspector changes can recompile shaders, so test runtime changes in a world build too. Projects with the VRChat Avatars SDK keep all features, even with both SDKs installed.

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

Optionally, define `VRCLV_FORCE_FULL_FEATURES` before the first Light Volumes include to ignore the Manager's **Shader Stripping** settings for this shader. Most integrations do not need it. Your own `VRCLV_DISABLE_*` macros still apply. These macros are compile-time choices, not runtime switches or material keywords.

Clustering requires shader target 3.5 or newer on D3D11, GLCore, Vulkan, GLES3 or Metal. Public sampling calls choose the appropriate light loop. `VRCLV_DISABLE_CLUSTERING` disables clustering for this shader, regardless of the Manager setting. Leave the internal `VRCLV_CLUSTERING_SUPPORTED` macro to the include.
