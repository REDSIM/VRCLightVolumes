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

Search for **Light Volume** in ASE's node menu:

| Node | Description |
| --- | --- |
| **Light Volume SH Specular** | Diffuse SH and individual highlights for PBR. Also includes approximate baked-volume highlights. |
| **Light Volume** | Diffuse SH for directional lighting, without individual highlights. |
| **Light Volume L0** | Ambient color for fog, particles and other surfaces that do not need light direction. |
| **Light Volume Evaluate** | Converts SH to diffuse lighting using the surface normal. |
| **Light Volume Specular** | Approximate highlights from existing SH. **Dominant Direction** gives one cheaper highlight. Do not add it on top of SH Specular. |
| **Is Light Volumes** | `1` when supported Light Volumes are enabled, `0` otherwise. |
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
- Use the full sample in place of the shader's probe lighting. Probe fallback is already included. Keep probes in the scene.
- For lightmapped meshes, keep your shader's existing lightmap lighting and add the **Additive** result. The examples below calculate only the Light Volume contribution.
- Keep your shader's direct lights, reflections, AO and emission.

> [!IMPORTANT]
> Calculate Light Volumes in the **Fragment** stage for avatars and most solid surfaces. Prefer the **Vertex** stage for small particles, foliage and other surfaces with heavy overdraw: avoiding repeated lighting calculations for overlapping pixels can be much faster. Vertex lighting is interpolated across triangles, so it loses small lighting details and cannot use the per-pixel normal map.

## Shipping Without The Package

The package include shown above requires VRC Light Volumes to be installed. Otherwise, your shader will not compile. To remove that dependency, distribute your own `LightVolumes.cginc` and `LightVolumesBuildConfig.cginc`.

`LightVolumesBuildConfig.cginc` tells [Shader Stripping](#shader-feature-stripping) which features to remove. The installed package manages its contents automatically.

- **Main code:** `LightVolumes.cginc` can sit beside your shader, be renamed or be copied directly into your shader code. Keep its config `#include` when copying the code.
- **Config:** keep `LightVolumesBuildConfig.cginc` as a separate file. By default, it sits beside `LightVolumes.cginc`. You can move it elsewhere under `Assets` or `Packages` if you update the config's `#include` path accordingly.

`LightVolumesBuildConfig.cginc` should contain:

```hlsl
// VRC Light Volumes: managed shader stripping config
#ifndef VRC_LIGHT_VOLUMES_BUILD_CONFIG_INCLUDED
#define VRC_LIGHT_VOLUMES_BUILD_CONFIG_INCLUDED

// Generated for the primary VRChat world scene. No disable tags means all features are available.

#endif
```

> [!IMPORTANT]
> Keep the exact filename and first-line comment: they identify a config the package may safely overwrite. Otherwise, automatic [Shader Stripping](#shader-feature-stripping) will not work. Keep the file writable and reset it to the template above before distribution. Do not ship your test world's generated exclusions.

If both files sit beside your shader, include the main file after `UnityCG.cginc`:

```hlsl
#include "UnityCG.cginc"
#include "LightVolumes.cginc"
```

### How LightVolumesBuildConfig Works

- **Without the package:** the template above keeps all features available. No extra defines or Manager are needed to compile the shader.
- **With the package:** Light Volumes updates every marked config automatically. World creators use **Light Volume Manager > [Shader Stripping](#shader-feature-stripping)**: **Auto** detects scene features. Turn it off to select features manually. Do not edit the generated config.
- **Play Mode and world builds:** [Shader Stripping](#shader-feature-stripping) applies when enabled. Entering Play Mode can recompile shaders. Edit Mode keeps all features to avoid recompiling after each scene change.
- **VRChat Avatars SDK projects:** all features stay available, even with the Worlds SDK also installed.

The receiving world supplies the lighting data at runtime.

## Directional Shading With Individual Speculars

Use this for PBR: Point, Spot and Area lights get physically based highlights with source size, cookies and shadows. Baked-volume highlights are included automatically as an SH approximation.

The Additive version includes only Additive volumes and Point/Spot/Area lights. It does not calculate highlights from Regular Volumes or the lightmap.

```hlsl
// Use your shader's world-space position and normal, with normal mapping applied.
float3 normalWS = normalize(worldNormal);
float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos); // Surface toward camera.
float3 worldPosOffset = 0; // Offset baked-volume sampling only. Leave zero normally.
float pointLightShading = 3; // Normal shaping: 0 = off, 1 = softer, higher = sharper.

// All colors are linear. Smoothness and metallic are in 0..1.
float3 L0, L1r, L1g, L1b, specular;

#ifdef LIGHTMAP_ON
    // Additive volumes and Point/Spot/Area lights. The shader already handles the lightmap.
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

// Evaluate directionality. A toon shader can apply its ramp here.
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

If you're making a stylized or toon shader, these helpers give you separate control over lighting and highlights. You get Regular, Additive and Point/Spot/Area lighting, plus separate volume and light speculars. The helpers already loop over lights and handle clustering, overdraw limits and shadows.

Place this in your fragment function. `normalWS` and `viewDir` must be normalized. `f0` is your material's specular color, such as `lerp(0.04, albedo, metallic)`.

```hlsl
float3 R0 = 0, R1r = 0, R1g = 0, R1b = 0;
float3 A0 = 0, A1r = 0, A1g = 0, A1b = 0;
float3 P0 = 0, P1r = 0, P1g = 0, P1b = 0;
float3 pointSpec = 0;
bool enabled = LightVolumesEnabled() > 0;

// Regular volumes or probe fallback. Skip this for lightmapped meshes.
#ifndef LIGHTMAP_ON
if (enabled) LV_LightVolumeRegularSH(worldPos, R0, R1r, R1g, R1b);
else LV_SampleLightProbe(R0, R1r, R1g, R1b);
#endif

// Additive volumes and local lights go into separate SH sets.
if (enabled) {
    LV_LightVolumeAdditiveSH(worldPos, A0, A1r, A1g, A1b);
    LV_PointLightVolumeSHSpecular(worldPos, normalWS, viewDir, smoothness, f0, 0, P0, P1r, P1g, P1b, pointSpec);
}

// Evaluate the diffuse groups and the combined volume/probe highlight.
float3 regular = LightVolumeEvaluate(normalWS, R0, R1r, R1g, R1b);
float3 additive = LightVolumeEvaluate(normalWS, A0, A1r, A1g, A1b);
float3 pointLights = LightVolumeEvaluate(normalWS, P0, P1r, P1g, P1b);
float3 volumeSpec = LightVolumeSpecularDominant(f0, smoothness, normalWS, viewDir, R0 + A0, R1r + A1r, R1g + A1g, R1b + A1b);

// Your code here: adjust each group, then combine with your material's diffuse color.

float3 volumeLighting = max(regular + additive + pointLights, 0) * diffuseColor;
volumeLighting += volumeSpec + pointSpec;
```

The `0` argument disables extra Point Light normal shaping for your own toon response. Shadows still apply to both `pointLights` and `pointSpec`. For lightmapped meshes, add `volumeLighting` to your existing lightmap lighting.

These internal `LV_*` helpers can change between releases. For finer control, see [LightVolumes.cginc](../Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc).

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

In your fragment function, the combined sampler calls your hook for each contributing light:

```hlsl
float3 L0, L1r, L1g, L1b, specular;
LightVolumeSHSpecular(worldPos, L0, L1r, L1g, L1b, specular, albedo, smoothness, metallic, normalWS, viewDir);
```

Use `LightVolumeAdditiveSHSpecular()` for the lightmapped branch. Supply normalized normal and view directions. The hook receives a normalized light direction. `l0` includes color, attenuation, cookie, shadow and normal-based masking. Return that light's final specular contribution. The caller adds it to the output.

`roughness` is squared perceptual roughness. `roughnessSq` is its square. `NoV` is the clamped normal/view dot product. `lightSpreadSq` describes source spread for size-aware highlights.

Keep shared material calculations outside the hook, which runs for each contributing light. Define it in source. No `shader_feature` or `multi_compile` keyword is needed.

## Shader feature stripping

Configure **Shader Stripping** on the **Light Volume Manager**. **Auto** checks the primary Manager's scene, including inactive and zero-intensity lights. It can't predict script changes or find features in other scenes and external prefabs.

If scripts add a feature later, turn **Auto** off and keep that feature enabled. Switching Point to Spot needs **Spot Lights**. Adding a Spot cookie needs **Spot Cookies**. Rotating a non-dynamic baked volume needs **Volume Rotation**. Keep parent features too, such as **Shadows** for any shadow-map type. Disable **Shader Stripping** to keep everything while testing.

The package writes these settings to every marked `LightVolumesBuildConfig.cginc`, including [bundled copies](#shipping-without-the-package). Udon can't restore code removed from a built shader. Play Mode Inspector changes can recompile shaders, so test runtime changes in a world build too.

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
