[VRC Light Volumes](../README.md) | **How to Use** | [Best Practices](../Documentation/BestPractices.md) | [Udon Sharp API](../Documentation/UdonSharpAPI.md) | [For Shader Developers](../Documentation/ForShaderDevelopers.md) | [Compatible Shaders](../Documentation/CompatibleShaders.md)

# How to Use

| Menu |
|----|
|[VRC Light Volumes System](../Documentation/HowToUse.md)|
|[Regular Light Volumes](../Documentation/HowToUse_RegularLightVolumes.md)|
|[Point Light Volumes](../Documentation/HowToUse_PointLightVolumes.md)|
|[Point Light Volume Shadows](../Documentation/HowToUse_Shadows.md)|
|**Point Light Material Sources**<br />&bull; [What This Material Does](#What-This-Material-Does)<br />&bull; [Quick Setup](#Quick-Setup)<br />&bull; [Material Rendering Contract](#Material-Rendering-Contract)<br />&bull; [Shadow Map Materials](#Shadow-Map-Materials)<br />&bull; [Cubemap Sources](#Cubemap-Sources)<br />&bull; [Single-Slice Sources](#Single-Slice-Sources)|
|[Area Light Emission](../Documentation/HowToUse_AreaLightEmission.md)|
|[Audio Link Integration](../Documentation/HowToUse_AudioLinkIntegration.md)|
|[TV Screens Integration](../Documentation/HowToUse_TVScreensIntegration.md)|
|[How Light Volumes Work?](../Documentation/HowToUse_HowItWorks.md)|

## Point Light Material Sources

A `Material` assigned to a Point Light Volume projection or shadow field is used as a texture generator. It is not rendered as a normal world material. The Light Volume Manager renders pass `0` of that material into one or more slices of the shared Point Light Volume texture arrays, and compatible shaders later sample those arrays while lighting the scene.

Use a Material source when the texture must be generated procedurally, combined from several inputs, copied from another runtime system, or updated every frame. For static images, regular Texture, Cubemap or Texture2DArray assets are cheaper and easier to debug.

## What This Material Does

Material sources are supported in these places:

- `Projection = Custom` on a **Point Light** uses the `Cubemap` field. A Material here generates a six-face cubemap cookie for point-light projection.
- `Projection = Custom` on a **Spot Light** uses the `Cookie` field. A Material here generates one projected cookie slice.
- **Area Light** `Cookie` can also use a Material source. It follows the same single-slice texture contract, but the lighting behavior is described in [Area Light Emission](../Documentation/HowToUse_AreaLightEmission.md).
- `Shadow Map` can use a Material source when `Shadows` is enabled. A Material here must generate EVSM shadow moments, not a regular black and white mask.

The same Material object is shared between lights when possible. If two lights need different generated output, create separate Material instances. Do not reuse one Material object for different outputs, because the manager deduplicates it into one generated texture source.

## Quick Setup

1. Create or pick a Material that draws the picture you want using regular `0..1` UVs, exactly as it would look on a simple quad.
2. Make sure the shader writes the result in pass `0`. Additional passes are ignored by the Light Volume Manager.
3. For a custom shader, use the usual blit setup: `Cull Off`, `ZWrite Off` and `ZTest Always`.
4. Assign the Material to the `Cubemap`, `Cookie` or `Shadow Map` field on the **Point Light Volume** authoring component.
5. For animated Materials or RenderTextures, enable `Auto Update Textures` in **Light Volume Setup**.
6. Use the lowest acceptable `Cookie Resolution` or `Shadow Resolution`, because cubemap sources consume six slices.

> [!IMPORTANT]
> A shadow Material is advanced. It must output the same EVSM data layout used by VRC Light Volumes. If you only need normal geometry-cast shadows, use `Bake Shadows` or the `Point Light Shadow Runtime Baker` instead.

## Material Rendering Contract

The manager renders the Material into a shared `Texture2DArray`. Think of it as drawing the Material into a small render texture, then using that texture as the light cookie or shadow source.

The shader gets regular `0..1` UVs from the blit, so pass `0` is rendered as if the Material was drawn on a simple quad.

The manager provides this extra shader property before rendering each Material source:

| Shader property | Meaning |
|----|----|
|`float4 _CustomRenderTextureInfo` | `x` = output width, `y` = output height, `z` = output array depth or `1` for cubemap face updates, `w` = output slice index or cubemap face index. |

Any other textures, colors or parameters must be normal Material properties declared by your shader and assigned on that Material.

The manager does not automatically pass the Point Light Volume color, intensity, transform, near plane or far clip to the Material. If your shader needs those values, expose normal Material properties such as `_Tint`, `_ShadowNearClip` or `_ShadowFarClip` and set them yourself.

For cookie projection, pass `0` should output regular linear color. The result is multiplied by the Point Light Volume `Color` and `Intensity` during lighting. Values above `1` are allowed for bright cookie areas, but keep the final light intensity under control to avoid clipping and banding on low precision targets.

Cookie alpha behavior depends on the light type:

| Light type | Cookie output |
|----|----|
| **Point Light** custom cubemap | RGB is used by lighting. Alpha is currently ignored, so write `1` unless you use it for your own intermediate workflow. |
| **Spot Light** custom cookie | RGB tints the light and alpha masks it. A transparent cookie pixel contributes no light. |
| **Area Light** cookie | Alpha is treated as an emission mask and the receiver uses `RGB * Alpha`. Keep alpha meaningful if the source has transparent parts. |

## Shadow Map Materials

VRC Light Volumes shadows are Exponential Variance Shadow Maps (EVSM). A shadow Material must output four channels:

| Channel | Required data |
|----|----|
|`R` | Positive warped depth moment. |
|`G` | Negative warped depth moment. |
|`B` | Square of the positive warped depth moment. |
|`A` | Square of the negative warped depth moment. |

The receiver compares these moments against the runtime fragment distance normalized by the Point Light Volume `Near Plane` and `Far Clip Plane`. A plain white/black shadow mask will not work correctly in the `Shadow Map` field.

Use the same EVSM encoding constants as the package:

```hlsl
float4 EncodeVRCLVShadowEVSM(float depth01) {
    depth01 = saturate(depth01) * 2.0 - 1.0;
    float positive = exp(5.54 * depth01);
    float negative = -exp(-5.0 * depth01);
    return float4(positive, negative, positive * positive, negative * negative);
}
```

`depth01` must be normalized with the same near/far range that the receiver uses:

```hlsl
float depth01 = (radialDistance - shadowNearClip) / max(shadowFarClip - shadowNearClip, 0.0001);
```

If the Point Light Volume `Far Clip Plane` is `0`, the authoring component resolves it from the light's current culling range. The Material does not receive that resolved value automatically, so either keep your Material's far clip property in sync manually or use a fixed manual `Far Clip Plane` for that light.

For a code reference, see `Packages/red.sim.lightvolumes/Shaders/Editor/PointLightShadowDepthEncode.shader`. It is the editor/runtime depth encoder used by the built-in shadow baker path.

## Cubemap Sources

Cubemap Material sources are rendered six times, once per cubemap face. If your shader ignores `_CustomRenderTextureInfo.w`, the same UV pattern is written to every face. That is fine for many stylized cookies.

Use `_CustomRenderTextureInfo.w` only when the shader needs different output per face, for example a procedural cubemap, a direction-based mask or a cubemap shadow source.

Cubemap face ID is stored in `_CustomRenderTextureInfo.w`:

| ID | Face |
|----|----|
|`0` | `+X` |
|`1` | `-X` |
|`2` | `+Y` |
|`3` | `-Y` |
|`4` | `+Z` |
|`5` | `-Z` |

For direction-based cubemap shaders, convert the UV and face index into a direction:

```hlsl
float3 CubemapDirection(float2 uv01, float face) {
    float2 uv = uv01 * 2.0 - 1.0;

    if (face < 0.5) return normalize(float3(1.0, -uv.y, -uv.x));
    if (face < 1.5) return normalize(float3(-1.0, -uv.y, uv.x));
    if (face < 2.5) return normalize(float3(uv.x, 1.0, uv.y));
    if (face < 3.5) return normalize(float3(uv.x, -1.0, -uv.y));
    if (face < 4.5) return normalize(float3(uv.x, -uv.y, 1.0));
    return normalize(float3(-uv.x, -uv.y, -1.0));
}
```

Then use that direction for your procedural math:

```hlsl
float face = floor(_CustomRenderTextureInfo.w + 0.5);
float3 direction = CubemapDirection(i.uv, face);
float3 cookieColor = abs(direction);
```

Cubemap projection is used by Point Light custom cookies and by cubemap shadow sources. Cubemap shadows are six times more expensive in texture slices than single-slice shadows, so keep their resolution conservative.

## Single-Slice Sources

Single-slice Material sources are rendered once into one texture-array slice. This is the simplest mode: normal `0..1` UVs become the cookie texture.

Single-slice projection is used by Spot Light cookies and Area Light cookies. For Spot Lights, the center of the texture is the light forward direction, and the visible cone maps to the texture rectangle.

`_CustomRenderTextureInfo.w` contains the final destination slice index. Most cookie shaders can ignore it.

Single-slice shadows are projected like a spotlight shadow camera. If a shadow Material writes EVSM data into one slice, the encoded depth must match the same projection, near plane and far clip used by the Point Light Volume.
