[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [Scripting API](./ScriptingAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Point Light Material Sources

| Menu |
| --- |
| [Overview](./HowToUse.md) |
| [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) |
| [Point Light Volumes](./HowToUse_PointLightVolumes.md) |
| [Froxel Clustering](./HowToUse_FroxelClustering.md) |
| [Shadows](./HowToUse_Shadows.md) |
| **Material Sources**<br />• [Assign A Material](#assign-a-material)<br />• [Cubemap Material Sources](#cubemap-material-sources)<br />• [Color And Alpha](#color-and-alpha)<br />• [Updates And Snapshots](#updates-and-snapshots)<br />• [Shadow Map Materials](#shadow-map-materials) |
| [Area Light Emission](./HowToUse_AreaLightEmission.md) |
| [AudioLink](./HowToUse_AudioLinkIntegration.md) |
| [TV Screens (Older Workflow)](./HowToUse_TVScreensIntegration.md) |
| [Debugging](./HowToUse_Debugging.md) |
| [How It Works](./HowToUse_HowItWorks.md) |

A Material can generate a cookie for any Point Light Volume type, including animated patterns and procedural effects. A **Spot Light** projects one 2D image. An **Area Light** emits light from one 2D image like a screen, spreading its colors into the surroundings. A **Point Light** projects a cubemap around itself.

## Assign A Material

Assign a Material directly to **Cookie** for a Spot or Area light, or **Cubemap** for a Point light. Point and Spot lights need **Projection → Custom** for these fields.

With **Projection → LUT**, a Material in **Falloff LUT** controls distance falloff; Spot lights also use horizontal cone falloff. Materials are also accepted in **Shadow Map** for [custom shadows](#shadow-map-materials).

Several lights can share one Material. Give them separate Materials when their image settings need to differ.

## Cubemap Material Sources

The Manager renders **pass 0** of your shader with `0..1` UVs. For a cubemap, it renders that pass six times, once per face. It supplies this shader property:

```hlsl
float4 _CustomRenderTextureInfo;
// x = output width, y = output height
// Cubemap: z = 1, w = face index (0..5)
// Single image: z = destination array depth, w = destination slice index
```

Face indices are `0 = +X`, `1 = -X`, `2 = +Y`, `3 = -Y`, `4 = +Z`, `5 = -Z`. Ignoring the face index repeats the same image on all six faces. For a single image, such as a Spot cookie or LUT, use the UVs directly. Its destination slice can change when the array is rebuilt, so do not use it as a stable light ID.

Use this shader as a starting point for your own Point light cookies. `CubemapDirection()` converts each face's UVs into a shared direction. The fragment function uses its spherical latitude to draw animated bands that continue across face boundaries.

```hlsl
Shader "Examples/Light Volume Spherical Bands" {
    Properties {
        _ColorA ("Color A", Color) = (1, 0.1, 0.05, 1)
        _ColorB ("Color B", Color) = (0.05, 0.2, 1, 1)
        _Speed ("Speed", Float) = 1
    }
    SubShader {
        Cull Off ZWrite Off ZTest Always
        Pass {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _ColorA, _ColorB;
            float _Speed;
            float4 _CustomRenderTextureInfo;

            // Convert this face's 0..1 UVs into the lookup direction.
            float3 CubemapDirection(float2 uv01, float face) {
                float2 uv = uv01 * 2.0 - 1.0;
                if (face < 0.5) return normalize(float3(1.0, -uv.y, -uv.x));
                if (face < 1.5) return normalize(float3(-1.0, -uv.y, uv.x));
                if (face < 2.5) return normalize(float3(uv.x, 1.0, uv.y));
                if (face < 3.5) return normalize(float3(uv.x, -1.0, -uv.y));
                if (face < 4.5) return normalize(float3(uv.x, -uv.y, 1.0));
                return normalize(float3(-uv.x, -uv.y, -1.0));
            }

            float4 frag(v2f_img i) : SV_Target {
                // The Manager sets w to the face being drawn.
                float3 direction = CubemapDirection(i.uv, _CustomRenderTextureInfo.w);
                // Latitude gives every face the same spherical pattern.
                float latitude = asin(clamp(direction.y, -1.0, 1.0));
                // Unity's time animates the bands.
                float bands = 0.5 + 0.5 * sin(latitude * 8.0 - _Time.y * _Speed);
                return float4(lerp(_ColorA.rgb, _ColorB.rgb, bands), 1.0);
            }
            ENDCG
        }
    }
}
```

Keep `Cull Off`, `ZWrite Off` and `ZTest Always` for material sources. For a Spot or Area cookie, replace the direction-based pattern with one drawn directly from `i.uv`.

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

## Shadow Map Materials

A custom **Shadow Map** Material describes the distance from the light to a shadow caster in each direction. It can generate animated or procedural shadows. For shadows cast by scene geometry, use the [built-in shadow baker](./HowToUse_Shadows.md).

The shader must output **EVSM (Exponential Variance Shadow Maps) moments**:

| Channel | Data |
| --- | --- |
| R | Positive warped depth. |
| G | Negative warped depth. |
| B | Square of R. |
| A | Square of G. |

Use this helper in your source shader to convert normalized radial depth into those four channels:

```hlsl
float4 EncodeVRCLVShadowEVSM(float depth01) {
    float depth = saturate(depth01) * 2.0 - 1.0;
    float positive = exp(5.54 * depth);
    float negative = -exp(-5.0 * depth);
    return float4(positive, negative, positive * positive, negative * negative);
}
```

In the pass-0 fragment function, encode the distance from the light to your shadow caster, including for projected Spot shadows:

```hlsl
// All distances use the same world units.
float depth01 = (radialDistance - shadowNearClip)
    / max(shadowFarClip - shadowNearClip, 0.0001);
return EncodeVRCLVShadowEVSM(depth01);
```

Supply `radialDistance`, `shadowNearClip` and `shadowFarClip` through your own shader calculations or Material properties. The Manager supplies `_CustomRenderTextureInfo`, but does not pass the light's position, rotation or near/far range to the Material.

Match the light's shadow projection, pose and depth range. A previous built-in bake can retain its far distance in `BakedFarClip`; use the range the shadow receiver actually uses. Convert perspective-camera depth to radial distance before encoding it. The package's [depth encoder](../Packages/red.sim.lightvolumes/Shaders/Editor/PointLightShadowDepthEncode.shader#L53) shows that conversion and the bake-bias calculation. A black-and-white mask or raw camera-depth texture will not work.

Point and Area shadows need six faces. Spot shadows use a single projected image, or six faces with **Force Cubemap Shadows**. For six-face Materials, use the same face indices and `CubemapDirection()` helper described above. The shadow layout is separate from the light's cookie layout.

> [!IMPORTANT]
> Use `float4` output for shadow data: it contains negative values and values above `1`. If an external Render Texture supplies the shadow, it must preserve linear floating-point data, such as **ARGBHalf** or **ARGBFloat**.
