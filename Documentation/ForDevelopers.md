[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [UdonSharp API](./UdonSharpAPI.md) | [Unity Editor API](./UnityEditorAPI.md) | **Shader Integration** | [Compatible Shaders](./CompatibleShaders.md)

# Shader Integration

Use this page to add Light Volumes to a Built-in Render Pipeline shader. The package includes shader code and Amplify Shader Editor examples in `Packages/red.sim.lightvolumes/Shaders/ASE Shaders`.

- [Amplify Shader Editor](#integrating-light-volumes-with-amplify-shader-editor-ase)
- [Diffuse and lightmap integration](#light-volume-integration-through-shader-code)
- [Specular lighting](#specular-lighting)
- [Function reference](#shader-functions)
- [Custom specular BRDF](#custom-specular-brdf)
- [Shader feature stripping](#shader-feature-stripping)

For runtime scripts, see [UdonSharp API](./UdonSharpAPI.md). For atlas processing and custom lightmappers, see [Unity Editor API](./UnityEditorAPI.md).

## Integrating Light Volumes with Amplify Shader Editor (ASE)

Start with [Light Volume PBR.shader](../Packages/red.sim.lightvolumes/Shaders/ASE%20Shaders/Light%20Volume%20PBR.shader). It disables the generated ambient contribution with `noambient` and adds the evaluated Light Volume lighting through Emission. Keep Unity's lightmap contribution on lightmapped objects and add only the additive Light Volume contribution there.

| Node | When to use it |
| --- | --- |
| **Light Volume** | Sample ambient color (`L0`) and directional lighting (`L1`). Feed the outputs to **Light Volume Evaluate**. |
| **Light Volume L0** | Sample only ambient color, for particles or fog where a surface normal is not useful. |
| **Light Volume Evaluate** | Turn L0/L1 into diffuse lighting using the surface normal. Multiply by the material's diffuse color. |
| **Light Volume Specular** | Approximate highlights from already sampled L0/L1. **Dominant Direction** uses one combined light direction instead of separate red, green and blue directions. |
| **Light Volume SH Specular** | Sample L0/L1 and specular together. Use for glossy surfaces that need separate Point, Spot and Area highlights, including their source sizes, cookies and shadows. |
| **Is Light Volumes** | Return `1` when a supported Light Volumes system is enabled; otherwise `0`. |
| **Light Volumes Version** | Read the scene's integration version. This is not the package version. |

On sampling nodes, enable **AdditiveOnly** for the lightmapped branch. It includes additive Regular Volumes and Point Light Volumes. Use a normalized **WorldNormal** and keep **WorldPositionOffset** at zero unless you deliberately want to shift baked-volume sampling. **PointLightShading** controls normal-based shaping; see [Normals and position offset](#normals-and-position-offset).

Add specular directly to the final lighting. Do not multiply it by albedo again. The SH Specular node is more expensive when many Point Light Volumes overlap; use the simpler nodes on matte materials and effects where the extra highlights are not visible.

## Light Volume integration through shader code

Use shader target 3.5 or newer for the full integration, as the supplied shaders do. This provides the texture-array support needed by projections and shadows. When the package is installed, include Unity's helpers first, then the package include:

```hlsl
#pragma target 3.5
#include "UnityCG.cginc"
#include "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc"
```

### Shipping a shader integration

You can use the installed package or distribute a copy of `LightVolumes.cginc` with your shader. The package path above uses the installed version and the world's shader-stripping settings.

For a standalone integration, such as an avatar shader that should not require a package installation, place a copy of `LightVolumes.cginc` next to your shader and use:

```hlsl
#include "UnityCG.cginc"
#define VRCLV_FORCE_FULL_FEATURES
#include "LightVolumes.cginc"
```

Define `VRCLV_FORCE_FULL_FEATURES` before the first Light Volumes include in each shader program. It skips the hardcoded package `LightVolumesBuildConfig.cginc` dependency and keeps the copied include portable. Without it, even a copied include still requires that package config file. The receiving world supplies the runtime lighting data; the shader's project does not need a Manager.

Update your copied include when adopting a newer integration version. Do not distribute a scene-generated stripping config captured during Play Mode or a world build. Explicit `VRCLV_DISABLE_*` macros in your shader still apply with `VRCLV_FORCE_FULL_FEATURES`.

### Basic diffuse lighting

Replace the shader's existing ambient/light-probe calculation with this code. In a hand-written forward shader, add this contribution in `ForwardBase` only: adding it in `ForwardAdd` would repeat it for each Unity light. The inputs are the fragment's world position, normalized world normal and material albedo:

```hlsl
float3 normalWS = normalize(worldNormal);
float3 L0, L1r, L1g, L1b;
LightVolumeSH(worldPos, L0, L1r, L1g, L1b, 0, normalWS);

float3 irradiance = max(LightVolumeEvaluate(normalWS, L0, L1r, L1g, L1b), 0);
float3 diffuseLighting = irradiance * albedo;
```

Combine `diffuseLighting` with your shader's direct lighting and other material effects. Remove the previous Light Probe contribution from that branch to avoid adding it twice. `LightVolumeSH()` supplies a Unity Light Probe fallback when Light Volumes are unavailable or too old for this include.

That fallback reads Unity's `unity_SHA*` terms; it does not evaluate the full L2 probe lighting used by `ShadeSH9`. To preserve your shader's exact original fallback, use its existing probe calculation when `LightVolumesEnabled() == 0`.

`LightVolumeEvaluate()` performs a linear L0/L1 evaluation. It does not clamp negative results, apply albedo, handle metallic diffuse reduction or apply ambient occlusion. The example clamps the result; keep your shader's existing material and energy-conservation rules when combining it.

### Additive Light Volumes for lightmapped geometry

A lightmap already contains the surface's baked lighting. Add only additive Light Volumes and Point Light Volumes to it:

```hlsl
float3 L0, L1r, L1g, L1b;
LightVolumeAdditiveSH(worldPos, L0, L1r, L1g, L1b, 0, normalWS);
float3 addedIrradiance = LightVolumeEvaluate(normalWS, L0, L1r, L1g, L1b);

// Both terms here are lighting, before multiplication by the material's diffuse color.
float3 diffuseLighting = max(decodedLightmap + addedIrradiance, 0) * albedo;
```

Use the regular call in the unlightmapped branch and the additive call in the lightmapped branch. The additive call returns zero when Light Volumes are unavailable. Do not add a second full `LightVolumeSH()` result on top of a lightmap.

### Normals and position offset

- `worldPos` is the actual shaded world position.
- `worldPosOffset` shifts only baked Regular and additive volume sampling. Point Light Volume range, projection and shadows still use the original `worldPos`.
- `worldNormal` is a normalized world-space surface normal. Use your final normal-mapped normal when appropriate. Do not scale it to change shading strength.
- `pointLightShading` controls the additional normal-based shaping of Point Light Volumes: `0` disables that shaping, `1` gives a softer transition, and values above `1` sharpen it. The normal-aware overloads default to `3`. Use non-negative values.
- Specular calls also need `viewDir`, normalized **from the surface toward the camera**: `normalize(_WorldSpaceCameraPos.xyz - worldPos)`.

The per-light **Shading Strength** setting and the shader's `pointLightShading` argument are different controls. Setting the shader argument to zero does not remove that light's shadow map. Setting the light's **Shading Strength** to zero disables its normal shaping and shadows.

The older overloads without `worldNormal` still compile. They pass zero for `pointLightShading`, preserving the older surface-shading behavior. Prefer the normal-aware calls for new surface shaders.

### Choosing where to sample

Fragment sampling follows small volumes, cookies and shadows most accurately. Vertex sampling can help high-overdraw particles or foliage, but interpolating the result may smear those details across large triangles. Test the actual mesh and target platform.

L0-only sampling suits fog and particles without meaningful surface direction. On solid objects it removes directional shading, so opposite sides can look equally bright. Use L1 for ordinary surfaces.

## Specular lighting

There are two choices:

| Approach | Result |
| --- | --- |
| `LightVolumeSH()` followed by `LightVolumeSpecular()` or `LightVolumeSpecularDominant()` | Approximate highlights from the combined SH lighting. Useful for an inexpensive or stylized result. Individual source shapes cannot be recovered from SH. |
| `LightVolumeSHSpecular()` | Sample once for diffuse and specular. Baked volumes use a dominant SH highlight; Point, Spot and Area lights get individual highlights with cookies, shadows and source-size broadening. |

For the second approach:

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

Cost grows with the number of contributing Point Light Volumes. The Manager's **Additive Max Overdraw** limits how many of them are accumulated at a pixel, including their individual speculars. It separately limits additive Regular Volumes.

## Shader Functions

All colors are linear. `L0` is ambient RGB. `L1r`, `L1g` and `L1b` store XYZ directional coefficients for the red, green and blue channels. Their lengths also carry lighting strength; do not normalize them before evaluation.

### Sampling diffuse SH

```hlsl
void LightVolumeSH(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b,
    float3 worldPosOffset, float3 worldNormal, float pointLightShading = 3);

void LightVolumeAdditiveSH(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b,
    float3 worldPosOffset, float3 worldNormal, float pointLightShading = 3);

float3 LightVolumeSH_L0(float3 worldPos, float3 worldPosOffset, float3 worldNormal, float pointLightShading = 3);
float3 LightVolumeAdditiveSH_L0(float3 worldPos, float3 worldPosOffset, float3 worldNormal, float pointLightShading = 3);
```

The full calls sample Regular Volumes, additive volumes and Point Light Volumes. The additive calls skip non-additive Regular Volumes. `_L0` calls return only ambient RGB; they are useful when you will discard directionality anyway.

The following compatibility overloads omit the normal and disable per-surface Point Light shaping:

```hlsl
void LightVolumeSH(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, float3 worldPosOffset = 0);
void LightVolumeAdditiveSH(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, float3 worldPosOffset = 0);
float3 LightVolumeSH_L0(float3 worldPos, float3 worldPosOffset = 0);
float3 LightVolumeAdditiveSH_L0(float3 worldPos, float3 worldPosOffset = 0);
```

### Evaluating diffuse SH

```hlsl
float3 LightVolumeEvaluate(float3 worldNormal, float3 L0, float3 L1r, float3 L1g, float3 L1b);
```

Returns `L0 + float3(dot(L1r, worldNormal), dot(L1g, worldNormal), dot(L1b, worldNormal))`. This makes it easy to replace with a toon ramp or another lighting model. Shadows are already included in sampled Point Light Volume data; there is no separate unshadowed output.

### Specular from existing SH

```hlsl
float3 LightVolumeSpecular(float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir,
    float3 L0, float3 L1r, float3 L1g, float3 L1b);
float3 LightVolumeSpecular(float3 f0, float smoothness, float3 worldNormal, float3 viewDir,
    float3 L0, float3 L1r, float3 L1g, float3 L1b);

float3 LightVolumeSpecularDominant(float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir,
    float3 L0, float3 L1r, float3 L1g, float3 L1b);
float3 LightVolumeSpecularDominant(float3 f0, float smoothness, float3 worldNormal, float3 viewDir,
    float3 L0, float3 L1r, float3 L1g, float3 L1b);
```

`LightVolumeSpecular()` uses separate red, green and blue directions. `LightVolumeSpecularDominant()` combines them into one direction and costs less. Choose by the look you need, rather than whether the object is an avatar or part of the world.

### Sampling SH and individual specular together

```hlsl
void LightVolumeSHSpecular(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b,
    out float3 specular, float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir,
    float3 worldPosOffset = 0, float pointLightShading = 3);
void LightVolumeSHSpecular(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b,
    out float3 specular, float3 f0, float smoothness, float3 worldNormal, float3 viewDir,
    float3 worldPosOffset = 0, float pointLightShading = 3);

void LightVolumeAdditiveSHSpecular(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b,
    out float3 specular, float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir,
    float3 worldPosOffset = 0, float pointLightShading = 3);
void LightVolumeAdditiveSHSpecular(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b,
    out float3 specular, float3 f0, float smoothness, float3 worldNormal, float3 viewDir,
    float3 worldPosOffset = 0, float pointLightShading = 3);
```

For every specular helper, `smoothness` and `metallic` use `0..1`. `f0` is the surface's reflection color when viewed straight on; use it for a specular-color workflow. The albedo/metallic overloads calculate `f0 = lerp(0.04, albedo, metallic)` for you.

### Availability and version

```hlsl
float LightVolumesEnabled();
float LightVolumesVersion();
```

`LightVolumesEnabled()` returns `1` only when the system is enabled and its integration version is supported by this include (version 2 or newer). The full sampling calls already handle fallback, so an extra check is usually unnecessary.

`LightVolumesVersion()` reports the scene's integration version: `0` when absent, `1` for legacy data, and `2` or `3` for those integration versions. It is not a package version or an enabled-state check. A version-1 scene falls back to Unity Light Probes with the current include.

### Separating regular, additive and Point Light data

Use the public functions above for normal integrations. Advanced toon shaders sometimes need separate ramps for the three light groups. The internal helpers below can provide those groups, but their signatures may change between package releases:

```hlsl
void LV_LightVolumeRegularSH(float3 worldPos, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b);
void LV_LightVolumeAdditiveSH(float3 worldPos, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b);
void LV_PointLightVolumeSH(float3 worldPos, float3 worldNormal, float pointLightShading,
    inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b);
```

Initialize each group's accumulators to zero. Call these only when `LightVolumesEnabled() > 0`; provide your own Light Probe fallback otherwise. Apply `worldPosOffset` only to the two voxel-volume calls. Point Light data already contains shadows and cookies. Pin and test the package version if you depend on `LV_*` helpers.

## Custom Specular BRDF

The `LV_CUSTOM_SPECULAR_BRDF` hook replaces individual Point, Spot and Area specular evaluation in `LightVolumeSHSpecular()` and `LightVolumeAdditiveSHSpecular()`. It does not replace diffuse evaluation or the baked-volume SH highlight.

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

The normal, view direction and light direction inputs are normalized. `l0` already includes the resolved light color, attenuation, cookie, shadow and per-surface mask. Return the final specular contribution for that light. The caller adds it to the output.

`roughness` is squared perceptual roughness; `roughnessSq` is its square. `NoV` is the clamped normal/view dot product. `lightSpreadSq` describes source spread for size-aware highlights. The simple example ignores roughness and source size; use them if your model needs those effects.

The function runs for every contributing light, so keep shared material calculations outside it. This is a source-code hook, not a material keyword; it does not need a new `shader_feature` or `multi_compile` option.

## Shader feature stripping

The Manager's **Shader Stripping** section removes unused Light Volumes code for Play Mode and world builds. Edit Mode compiles all features. **Auto** takes a snapshot of the primary Manager's scene before Play Mode or a build, including inactive and zero-intensity lights. It does not inspect your scripts, prefabs outside that scene or other loaded scenes.

If scripts introduce a new light type, cookie, shadow layout or rotated volume later, turn **Auto** off and retain the required features manually. For example, a Point light switched to Spot at runtime needs **Spot Lights** too; assigning a cookie to a plain Spot needs **Spot Cookies**; rotating a non-dynamic baked volume needs **Volume Rotation**. Also preserve parent features such as **Shadows** for a shadow-map type. You can disable **Shader Stripping** to keep everything while testing.

The generated `LightVolumesBuildConfig.cginc` is managed by the package. Do not edit it by hand. Runtime Udon field changes cannot restore code that was removed from the built shader. Inspector changes in Play Mode can recompile the Editor's shader, which does not prove the feature was retained in a world build. Projects containing the VRChat Avatars SDK keep all features, including projects with both SDKs installed.

Shader authors can make explicit source-level exclusions before the include:

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

Clustering requires shader target 3.5 or newer on D3D11, GLCore, Vulkan, GLES3 or Metal. Public sampling functions select clustered or sequential traversal automatically. `VRCLV_DISABLE_CLUSTERING` forces the sequential path; a Manager setting cannot re-enable it. Do not define the internal capability macro `VRCLV_CLUSTERING_SUPPORTED` yourself.
