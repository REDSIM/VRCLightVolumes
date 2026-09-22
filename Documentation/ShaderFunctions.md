[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [Scripting API](./ScriptingAPI.md) | **Shader Integration** | [Compatible Shaders](./CompatibleShaders.md)

# Shader Functions

| Menu |
| --- |
| [Shader Integration](./ForDevelopers.md) |
| **Shader Functions**<br />• [LightVolumeSHSpecular](#void-lightvolumeshspecular)<br />• [LightVolumeAdditiveSHSpecular](#void-lightvolumeadditiveshspecular)<br />• [LightVolumeSH](#void-lightvolumesh)<br />• [LightVolumeAdditiveSH](#void-lightvolumeadditivesh)<br />• [LightVolumeSH_L0](#float3-lightvolumesh_l0)<br />• [LightVolumeAdditiveSH_L0](#float3-lightvolumeadditivesh_l0)<br />• [LightVolumeEvaluate](#float3-lightvolumeevaluate)<br />• [LightVolumeSpecular](#float3-lightvolumespecular)<br />• [LightVolumeSpecularDominant](#float3-lightvolumespeculardominant)<br />• [LightVolumesEnabled](#float-lightvolumesenabled)<br />• [LightVolumesVersion](#float-lightvolumesversion) |

For setup and complete examples, see [Shader Integration](./ForDevelopers.md#light-volume-integration-through-shader-code).

## void LightVolumeSHSpecular()

Samples Regular and Additive volumes with their combined dominant-direction highlight, then adds individual Point, Spot and Area lighting. When unavailable, it uses Unity's probe SH for diffuse lighting and an approximate highlight; it does not sample Reflection Probe cubemaps.

```hlsl
// Sample the lighting and collect its SH coefficients and highlights.
float3 L0, L1r, L1g, L1b, specular;
LightVolumeSHSpecular(worldPos, L0, L1r, L1g, L1b, specular, albedo, smoothness, metallic, normalWS, viewDir);
float3 irradiance = LightVolumeEvaluate(normalWS, L0, L1r, L1g, L1b);
// diffuseColor includes your material's metallic/energy adjustments. Specular is already colored.
float3 lighting = max(irradiance, 0) * diffuseColor + specular;
```

| Function argument | Description |
| --- | --- |
| `float3 worldPos` | Surface position in world space. |
| `out float3 L0` | Outputs linear ambient RGB. |
| `out float3 L1r`<br />`out float3 L1g`<br />`out float3 L1b` | Outputs RGB light directions and strength. Do not normalize them. |
| `out float3 specular` | Outputs all highlights, including the material's specular color. Add directly; do not multiply by albedo again. |
| `float3 albedo` | Linear material albedo. |
| `float smoothness` | Surface smoothness, `0..1`. |
| `float metallic` | Metalness, `0..1`. Together with albedo, gives `f0 = lerp(0.04, albedo, metallic)`. |
| `float3 worldNormal` | Normalized world-space normal, including the normal map if used. |
| `float3 viewDir` | Normalized world-space direction from the surface toward the camera. |
| `float3 worldPosOffset` | Optional baked-volume sample offset in world units; default `0`. Does not offset Point Lights. |
| `float pointLightShading` | Optional normal shaping for Point Lights: `0` off, `1` soft, default `3`. Use non-negative values. Shadows still work when this is `0`. |
| `float3 f0` | Alternative linear specular color at a head-on view. Use `f0, smoothness` instead of `albedo, smoothness, metallic`. |

For the specular-color overload, reuse the same outputs:

```hlsl
LightVolumeSHSpecular(worldPos, L0, L1r, L1g, L1b, specular, f0, smoothness, normalWS, viewDir);
```

## void LightVolumeAdditiveSHSpecular()

Samples Additive volumes with their dominant-direction highlight, plus individual Point, Spot and Area lighting. It excludes non-additive Regular Volumes and ordinary probes; all outputs are zero when Light Volumes is unavailable.

```hlsl
// Sample the lighting and collect its SH coefficients and highlights.
float3 L0, L1r, L1g, L1b, specular;
LightVolumeAdditiveSHSpecular(worldPos, L0, L1r, L1g, L1b, specular, albedo, smoothness, metallic, normalWS, viewDir);
// Add to decoded lightmap lighting before applying the material's diffuse color.
float3 irradiance = decodedLightmap + LightVolumeEvaluate(normalWS, L0, L1r, L1g, L1b);
float3 lighting = max(irradiance, 0) * diffuseColor + specular;
```

| Function argument | Description |
| --- | --- |
| `float3 worldPos` | Surface position in world space. |
| `out float3 L0` | Outputs linear ambient RGB from the added lights. |
| `out float3 L1r`<br />`out float3 L1g`<br />`out float3 L1b` | Outputs RGB light directions and strength. Do not normalize them. |
| `out float3 specular` | Outputs Additive-volume and individual-light highlights, already colored. Add directly. |
| `float3 albedo` | Linear material albedo. |
| `float smoothness` | Surface smoothness, `0..1`. |
| `float metallic` | Metalness, `0..1`. Together with albedo, gives `f0 = lerp(0.04, albedo, metallic)`. |
| `float3 worldNormal` | Normalized world-space normal, including the normal map if used. |
| `float3 viewDir` | Normalized world-space direction from the surface toward the camera. |
| `float3 worldPosOffset` | Optional Additive-volume sample offset in world units; default `0`. Does not offset Point Lights. |
| `float pointLightShading` | Optional normal shaping for Point Lights: `0` off, `1` soft, default `3`. Use non-negative values. Shadows still work when this is `0`. |
| `float3 f0` | Alternative linear specular color at a head-on view. Use `f0, smoothness` instead of `albedo, smoothness, metallic`. |

```hlsl
LightVolumeAdditiveSHSpecular(worldPos, L0, L1r, L1g, L1b, specular, f0, smoothness, normalWS, viewDir);
```

## void LightVolumeSH()

Samples diffuse SH from Regular, Additive and Point Light Volumes, with Unity probe fallback when Light Volumes is unavailable. Evaluate the outputs for diffuse lighting or reuse them for approximate SH highlights.

```hlsl
// Sample the lighting and collect its SH coefficients.
float3 L0, L1r, L1g, L1b;
LightVolumeSH(worldPos, L0, L1r, L1g, L1b, 0, normalWS);
float3 irradiance = LightVolumeEvaluate(normalWS, L0, L1r, L1g, L1b);
float3 diffuseLighting = max(irradiance, 0) * diffuseColor;
```

| Function argument | Description |
| --- | --- |
| `float3 worldPos` | Surface position in world space. |
| `out float3 L0` | Outputs linear ambient RGB. |
| `out float3 L1r`<br />`out float3 L1g`<br />`out float3 L1b` | Outputs RGB light directions and strength. Do not normalize them. |
| `float3 worldPosOffset` | Baked-volume sample offset in world units. Use `0` for the actual surface position. Does not offset Point Lights. |
| `float3 worldNormal` | Normalized world-space normal, including the normal map if used. |
| `float pointLightShading` | Optional normal shaping for Point Lights: `0` off, `1` soft, default `3`. Use non-negative values. Shadows still work when this is `0`. |

The overload without `worldNormal` accepts optional `worldPosOffset = 0` after the outputs. It disables Point Light normal shaping; use the normal-aware call for surfaces.

## void LightVolumeAdditiveSH()

Samples diffuse SH from Additive volumes and Point, Spot and Area lights, without the Regular volume/probe baseline. All outputs are zero when Light Volumes is unavailable.

```hlsl
// Sample the lighting and collect its SH coefficients.
float3 L0, L1r, L1g, L1b;
LightVolumeAdditiveSH(worldPos, L0, L1r, L1g, L1b, 0, normalWS);
// Add to the decoded lightmap before applying the material's diffuse color.
float3 irradiance = decodedLightmap + LightVolumeEvaluate(normalWS, L0, L1r, L1g, L1b);
float3 diffuseLighting = max(irradiance, 0) * diffuseColor;
```

| Function argument | Description |
| --- | --- |
| `float3 worldPos` | Surface position in world space. |
| `out float3 L0` | Outputs linear ambient RGB from the added lights. |
| `out float3 L1r`<br />`out float3 L1g`<br />`out float3 L1b` | Outputs RGB light directions and strength. Do not normalize them. |
| `float3 worldPosOffset` | Additive-volume sample offset in world units. Use `0` for the actual surface position. Does not offset Point Lights. |
| `float3 worldNormal` | Normalized world-space normal, including the normal map if used. |
| `float pointLightShading` | Optional normal shaping for Point Lights: `0` off, `1` soft, default `3`. Use non-negative values. Shadows still work when this is `0`. |

The overload without `worldNormal` accepts optional `worldPosOffset = 0` after the outputs. It disables Point Light normal shaping but keeps shadows.

## float3 LightVolumeSH_L0()

Returns linear ambient RGB from all Light Volume types, with Unity probe ambient fallback. Use it for particles or fog that do not need light direction; cookies and shadows still apply.

```hlsl
float3 particleLighting = max(LightVolumeSH_L0(worldPos), 0) * particleColor;
```

| Function argument | Description |
| --- | --- |
| `float3 worldPos` | Position to light, in world space. |
| `float3 worldPosOffset` | Baked-volume sample offset in world units; default `0` in the short overload. Does not offset Point Lights. |
| `float3 worldNormal` | Normalized world-space normal, in the normal-aware overload only. |
| `float pointLightShading` | Normal-aware overload: optional Point Light normal shaping, default `3`; `0` off and `1` soft. Use non-negative values. |

The short overload has no normal shaping. To keep that shaping while returning only ambient RGB, supply offset and normal:

```hlsl
float3 ambient = LightVolumeSH_L0(worldPos, 0, normalWS, 1);
```

## float3 LightVolumeAdditiveSH_L0()

Returns linear ambient RGB from Additive volumes and Point, Spot and Area lights, including cookies and shadows. Returns zero when Light Volumes is unavailable.

```hlsl
float3 extraAmbient = LightVolumeAdditiveSH_L0(worldPos);
float3 diffuseLighting = max(decodedLightmap + extraAmbient, 0) * diffuseColor;
```

| Function argument | Description |
| --- | --- |
| `float3 worldPos` | Position to light, in world space. |
| `float3 worldPosOffset` | Additive-volume sample offset in world units; default `0` in the short overload. Does not offset Point Lights. |
| `float3 worldNormal` | Normalized world-space normal, in the normal-aware overload only. |
| `float pointLightShading` | Normal-aware overload: optional Point Light normal shaping, default `3`; `0` off and `1` soft. Use non-negative values. |

The short overload has no normal shaping. The normal-aware overload takes `worldPos, worldPosOffset, worldNormal`, then optional `pointLightShading`.

## float3 LightVolumeEvaluate()

Converts sampled SH into diffuse lighting using `L0 + float3(dot(L1r, worldNormal), dot(L1g, worldNormal), dot(L1b, worldNormal))`. It does not sample textures, clamp negative values or apply material color.

```hlsl
float3 irradiance = LightVolumeEvaluate(normalWS, L0, L1r, L1g, L1b);
float3 diffuseLighting = max(irradiance, 0) * diffuseColor;
```

| Function argument | Description |
| --- | --- |
| `float3 worldNormal` | Normalized world-space surface normal. |
| `float3 L0` | Sampled linear ambient RGB. |
| `float3 L1r`<br />`float3 L1g`<br />`float3 L1b` | Sampled RGB directional coefficients, with their original lengths. |

Point Light SH already includes shadows; this function does not recover unshadowed lighting.

## float3 LightVolumeSpecular()

Approximates highlights from the separate red, green and blue directions in existing SH, including Unity probe SH. It does not resample the scene or recover individual source sizes.

```hlsl
float3 specular = LightVolumeSpecular(albedo, smoothness, metallic, normalWS, viewDir, L0, L1r, L1g, L1b);
// Already includes the material's specular color; add without multiplying by albedo.
lighting += specular;
```

| Function argument | Description |
| --- | --- |
| `float3 albedo` | Linear material albedo. |
| `float smoothness` | Surface smoothness, `0..1`. |
| `float metallic` | Metalness, `0..1`. Together with albedo, gives `f0 = lerp(0.04, albedo, metallic)`. |
| `float3 worldNormal` | Normalized world-space surface normal. |
| `float3 viewDir` | Normalized world-space direction from the surface toward the camera. |
| `float3 L0` | Sampled linear ambient RGB. |
| `float3 L1r`<br />`float3 L1g`<br />`float3 L1b` | Sampled RGB directional coefficients. Do not normalize them. |
| `float3 f0` | Alternative linear specular color at a head-on view. Use `f0, smoothness` instead of `albedo, smoothness, metallic`. |

```hlsl
float3 specular = LightVolumeSpecular(f0, smoothness, normalWS, viewDir, L0, L1r, L1g, L1b);
```

## float3 LightVolumeSpecularDominant()

Approximates one highlight from the combined RGB light direction in existing SH. This is cheaper than `LightVolumeSpecular()`; use either helper instead of adding another highlight to the combined sampler's specular output.

```hlsl
float3 specular = LightVolumeSpecularDominant(albedo, smoothness, metallic, normalWS, viewDir, L0, L1r, L1g, L1b);
lighting += specular;
```

| Function argument | Description |
| --- | --- |
| `float3 albedo` | Linear material albedo. |
| `float smoothness` | Surface smoothness, `0..1`. |
| `float metallic` | Metalness, `0..1`. Together with albedo, gives `f0 = lerp(0.04, albedo, metallic)`. |
| `float3 worldNormal` | Normalized world-space surface normal. |
| `float3 viewDir` | Normalized world-space direction from the surface toward the camera. |
| `float3 L0` | Sampled linear ambient RGB. |
| `float3 L1r`<br />`float3 L1g`<br />`float3 L1b` | Sampled RGB directional coefficients. Do not normalize them. |
| `float3 f0` | Alternative linear specular color at a head-on view. Use `f0, smoothness` instead of `albedo, smoothness, metallic`. |

The result already includes the material's specular color. The specular-color overload is:

```hlsl
float3 specular = LightVolumeSpecularDominant(f0, smoothness, normalWS, viewDir, L0, L1r, L1g, L1b);
```

## float LightVolumesEnabled()

Returns `1` when Light Volumes is enabled with integration version 2 or newer; otherwise `0`. Full samplers already provide probe fallback, so use this check only to keep a different fallback calculation:

```hlsl
float3 irradiance = probeLighting; // Your shader's existing probe calculation.
if (LightVolumesEnabled() > 0) {
    // Replace probe lighting only when Light Volumes is available.
    float3 L0, L1r, L1g, L1b;
    LightVolumeSH(worldPos, L0, L1r, L1g, L1b, 0, normalWS);
    irradiance = max(LightVolumeEvaluate(normalWS, L0, L1r, L1g, L1b), 0);
}
```

No arguments.

The built-in fallback reads Unity's `unity_SHA*` terms, without the full L2 result of `ShadeSH9`.

## float LightVolumesVersion()

Returns the scene's integration version: `0` when absent, `1` for legacy data, or the supplied version such as `2` or `3`. This is not the package version; use `LightVolumesEnabled()` to check availability.

```hlsl
bool usesVersion3Data = LightVolumesVersion() >= 3;
```

No arguments.

Version-1 scenes use the probe fallback with this include.
