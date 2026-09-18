[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [Scripting API](./ScriptingAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Point Light Material Sources

| Menu |
| --- |
| [Overview](./HowToUse.md) |
| [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) |
| [Point Light Volumes](./HowToUse_PointLightVolumes.md) |
| [Froxel Clustering](./HowToUse_FroxelClustering.md) |
| [Shadows](./HowToUse_Shadows.md) |
| **Material Sources**<br />• [Assign A Material](#assign-a-material)<br />• [Example: Animated Colored Stripes](#example-animated-colored-stripes)<br />• [Color And Alpha](#color-and-alpha)<br />• [Updates And Snapshots](#updates-and-snapshots)<br />• [Cubemap Sources](#cubemap-sources)<br />• [Shadow Map Materials](#shadow-map-materials) |
| [Area Light Emission](./HowToUse_AreaLightEmission.md) |
| [AudioLink](./HowToUse_AudioLinkIntegration.md) |
| [TV Screens (Older Workflow)](./HowToUse_TVScreensIntegration.md) |
| [Debugging](./HowToUse_Debugging.md) |
| [How It Works](./HowToUse_HowItWorks.md) |

A Material source generates a light's image. Use it for animated patterns or procedural effects. Use a texture for a fixed image.

Start with an unlit image shader, such as the example below. A screen's usual lit material may not produce the same image when used as a light source.

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

Several lights can share one Material. Give them separate Materials when their image settings need to differ.

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

## Color And Alpha

Output **linear color** from the source shader. The light applies its own Color and Intensity afterward. HDR colors are supported.

| Light type | Output interpretation |
| --- | --- |
| Point custom cubemap | RGB lights the scene; alpha is ignored. |
| Spot custom cookie | RGB lights the scene; alpha masks the contribution. |
| Area cookie | Emission is `RGB × Alpha`. |

Write alpha `1` for a fully emitting Spot or Area image. A shader that returns zero alpha may look bright in an RGB preview but produce no light.

Use the Manager's **Cookie Resolution** to choose image detail. Cubemap sources generate six images per update, so keep animated ones simple.

## Updates And Snapshots

Keep Manager **Auto Update Textures** enabled for animated Material, Render Texture and Custom Render Texture sources.

In these Udon calls, `Lamp` is your assigned `PointLightVolumeInstance` and `cookieMaterial` is the source Material.

`SetCustomMaterial(Material material)` enables live projection updates:

```csharp
Lamp.SetCustomMaterial(cookieMaterial);
```

Alternatively, use `SetCustomMaterial(Material material, bool autoUpdate)` with `false` to keep a snapshot until the shared array is rebuilt:

```csharp
Lamp.SetCustomMaterial(cookieMaterial, false);
```

Equivalent texture overloads are listed in the [UdonSharp API](./ScriptingAPI.md#pointlightvolumeinstance).

For animation, change the existing Material's parameters instead of assigning a new Material every frame.

## Cubemap Sources

A Point Light Material draws six cubemap faces. For a direction-based image, use the [cubemap shader example](./TechnicalDetails.md#cubemap-material-sources).

## Shadow Map Materials

For shadows from scene objects, use the [shadow baker](./HowToUse_Shadows.md). A custom Shadow Map Material needs encoded depth data; a black-and-white image won't work. See the [shader requirements and example](./TechnicalDetails.md#shadow-map-materials).
