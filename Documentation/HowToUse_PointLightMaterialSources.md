[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [UdonSharp API](./UdonSharpAPI.md) | [Unity Editor API](./UnityEditorAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Point Light Material Sources

**Guides:** [Overview](./HowToUse.md) · [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) · [Point Light Volumes](./HowToUse_PointLightVolumes.md) · [Froxel Clustering](./HowToUse_FroxelClustering.md) · [Shadows](./HowToUse_Shadows.md) · **Material Sources** · [Area Light Emission](./HowToUse_AreaLightEmission.md) · [AudioLink](./HowToUse_AudioLinkIntegration.md) · [TV Screens (Older Workflow)](./HowToUse_TVScreensIntegration.md) · [Debugging](./HowToUse_Debugging.md) · [How It Works](./HowToUse_HowItWorks.md)

A Material source generates the image used by a light. Use it for animated patterns, procedural emission or copying an image from another system. A static texture is simpler when the image never changes.

The Manager draws **pass 0** of the Material into the light's shared texture array. It does not render a screen mesh or reproduce the appearance of a lit world material. Start with an unlit shader that draws the intended emission image using `0..1` UVs.

## Assign A Material

| Light setting | Field | Material output |
| --- | --- | --- |
| Point or Spot, **Projection → LUT** | **Falloff LUT** | Distance falloff; Spot lights also use horizontal cone falloff. |
| Point, **Projection → Custom** | **Cubemap** | Six faces of a colored light pattern. |
| Spot, **Projection → Custom** | **Cookie** | One projected image. |
| Area | **Cookie** | One rectangular emission image. |
| Custom shadow source (advanced) | **Shadow Map** | Special EVSM depth data; see [Shadow Map Materials](#shadow-map-materials). |

1. Create a Material with a shader that generates the required image. For a first test, follow [Animated Colored Stripes](#example-animated-colored-stripes) below.
2. Assign it to the appropriate field above.
3. Leave Manager **Auto Update Textures** enabled for animation.
4. Check the light on a surface with a compatible shader. Adjust the light's Color and Intensity separately from the generated image.

The same Material object can be shared by several lights. Its output is generated once per compatible shared entry, then each light applies its own brightness, color and transform. If two lights need different Material parameters, give them separate Material objects.

## Example: Animated Colored Stripes

This example produces moving colored stripes without an input texture:

1. In the Project window, use **Assets → Create → Shader → Unlit Shader** and name the asset `LightVolumeStripes`.
2. Open `LightVolumeStripes.shader`, replace its contents with the code below, and save it. Return to Unity and let the shader import.
3. Use **Assets → Create → Material**. In the Material's Shader dropdown, choose **Examples → Light Volume Stripes**.
4. Create a Spot or Area Light Volume and check that it lights a nearby surface. For a Spot, choose **Projection → Custom**.
5. Drag the new Material into the light's **Cookie** field. Keep the light's Color white and Manager **Auto Update Textures** enabled.
6. Enter Play Mode. Colored stripes should move across a Spot's projected image, or change the colors emitted by an Area Light. Adjust **Color A**, **Color B** and **Speed** on the source Material.

```hlsl
Shader "Examples/Light Volume Stripes" {
    Properties {
        _ColorA ("Color A", Color) = (1, 0.1, 0.05, 1)
        _ColorB ("Color B", Color) = (0.05, 0.2, 1, 1)
        _Speed ("Speed", Float) = 0.1
    }
    SubShader {
        Cull Off ZWrite Off ZTest Always
        Pass {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _ColorA;
            float4 _ColorB;
            float _Speed;

            float4 frag(v2f_img i) : SV_Target {
                float stripe = step(0.5, frac(i.uv.x * 4.0 - _Time.y * _Speed));
                return float4(lerp(_ColorA.rgb, _ColorB.rgb, stripe), 1.0);
            }
            ENDCG
        }
    }
}
```

For your own source shader, draw the image in **pass 0** with `0..1` UVs and use `Cull Off`, `ZWrite Off` and `ZTest Always`, as in the example.

The Material's own texture/color properties remain available, including an assigned `_MainTex`. The Manager does not automatically supply the light's Color, Intensity, position or shadow clip planes. Expose and set any such inputs yourself.

## Color And Alpha

Cookie output is **linear color**. The light multiplies it by Color and Intensity. HDR values above `1` are supported within the half-precision cookie array's range.

| Light type | Output interpretation |
| --- | --- |
| Point custom cubemap | RGB lights the scene; alpha is ignored. |
| Spot custom cookie | RGB lights the scene; alpha masks the contribution. |
| Area cookie | Emission is `RGB × Alpha`. |

Write alpha `1` for a fully emitting Spot or Area image. A shader that returns zero alpha may look bright in an RGB preview but produce no light.

**Cookie Resolution** controls the output resolution for projection sources. **Shadow Resolution** controls the shared shadow output. Cubemap output uses six slices, so each refresh draws pass 0 six times.

## Updates And Snapshots

Material, Render Texture and Custom Render Texture sources default to live updates. Manager **Auto Update Textures** must also be enabled to refresh them. Static texture assets are copied only when the array is built or rebuilt.

From Udon, `SetCustomMaterial(material)` enables live projection updates. `SetCustomMaterial(material, false)` keeps a snapshot from the latest rebuild. Equivalent texture overloads are listed in the [UdonSharp API](./UdonSharpAPI.md#pointlightvolumeinstance).

Change parameters on the existing Material for continuous effects. Replacing the source requests an array rebuild. A source used with both live and snapshot modes needs separate entries.

<details>
<summary>Advanced: cubemap and shadow source shaders</summary>

## Cubemap Sources

For Point cookies and cubemap shadows, the Manager renders the Material once for each face. It supplies:

```hlsl
float4 _CustomRenderTextureInfo;
// x = output width, y = output height
// Cubemap: z = 1, w = face index
// Single slice: z = array depth, w = destination slice index
```

A single-slice cookie normally ignores this property. Do not use its destination slice index as a stable light ID; array rebuilds can change it.

For cubemaps, `_CustomRenderTextureInfo.w` is `0: +X`, `1: -X`, `2: +Y`, `3: -Y`, `4: +Z`, `5: -Z`. Ignoring it writes the same image to all six faces. To generate a direction-based pattern, convert face UVs with:

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

For example, `abs(CubemapDirection(i.uv, _CustomRenderTextureInfo.w))` gives RGB colors based on direction. A procedural pattern based on this direction can cross face boundaries consistently.

## Shadow Map Materials

Use a shadow Material only if you are implementing your own shadow source. For geometry-cast shadows, use the [built-in shadow baker](./HowToUse_Shadows.md).

The shader must output **EVSM moments**, not a black-and-white visibility mask or raw depth:

| Channel | Data |
| --- | --- |
| R | Positive warped depth. |
| G | Negative warped depth. |
| B | Square of R. |
| A | Square of G. |

The package's encoding is:

```hlsl
float4 EncodeVRCLVShadowEVSM(float depth01) {
    float depth = saturate(depth01) * 2.0 - 1.0;
    float positive = exp(5.54 * depth);
    float negative = -exp(-5.0 * depth);
    return float4(positive, negative, positive * positive, negative * negative);
}
```

Use **radial distance from the light**, including for a projected Spot shadow:

```hlsl
float depth01 = (radialDistance - shadowNearClip)
    / max(shadowFarClip - shadowNearClip, 0.0001);
```

The projection, bake pose and near/far range must match the light's shadow receiver. A raw perspective-camera depth value is not a radial distance and must be converted first. See [PointLightShadowDepthEncode.shader](../Packages/red.sim.lightvolumes/Shaders/Editor/PointLightShadowDepthEncode.shader) for the package's conversion and bias handling.

For an external source, an explicit **Far Plane** is easiest to keep in sync. `0 (Auto)` resolves a range from the light, but that value is not automatically passed to your Material. Single-view Spot shadows must match the Spot shadow-camera projection; cubemap shadows must provide all six matching faces.

</details>
