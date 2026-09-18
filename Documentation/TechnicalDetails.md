[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [Scripting API](./ScriptingAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Technical Details

Use this reference when writing custom light-source shaders or extending Light Volumes. It lists supported lightmappers and covers data formats, shader requirements and runtime shadow behavior.

For scene setup, use these guides:

- [Material Sources](./HowToUse_PointLightMaterialSources.md)
- [Froxel Clustering](./HowToUse_FroxelClustering.md)
- [AudioLink](./HowToUse_AudioLinkIntegration.md)
- [Shadows](./HowToUse_Shadows.md)

## Supported Lightmappers

| Lightmapper |
| --- |
| [Unity Progressive](https://docs.unity3d.com/2022.3/Documentation/Manual/progressive-lightmapper.html) |
| [Bakery](https://geom.io/bakery/wiki/index.php?title=Main_Page) |
| [Hikari](https://doc.suzufactory.com/Hikari/) |
| [Glim](https://github.com/z3y/glim) |

## Cubemap Material Sources

The Manager renders a Material once per face for Point cookies and cubemap shadows.
It supplies this property:

```hlsl
float4 _CustomRenderTextureInfo;
// x = output width, y = output height
// Cubemap: z = 1, w = face index
// Single slice: z = array depth, w = destination slice index
```

A single-slice cookie usually ignores this property.
The destination slice index can change after an array rebuild.
Use a separate identifier when an integration needs a stable light ID.

The cubemap face order is:

| Index | Face |
| --- | --- |
| 0 | +X |
| 1 | -X |
| 2 | +Y |
| 3 | -Y |
| 4 | +Z |
| 5 | -Z |

A Material that ignores the face index writes the same image to all six faces.
The `CubemapDirection()` helper below converts `0..1` UVs and a face index to a normalized direction.
Copy it into your source shader before the fragment function:

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

In the pass-0 fragment function, use the direction to draw a pattern across all faces.
For example, with `i.uv` in `0..1`:

```hlsl
float3 direction = CubemapDirection(i.uv, _CustomRenderTextureInfo.w);
return float4(abs(direction), 1);
```

This produces RGB colors from the direction and remains continuous across face boundaries.

Sources: [material output](../Packages/red.sim.lightvolumes/UScripts/LightVolumeManager.Textures.cs#L1092), [receiver face convention](../Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc#L422).

## Shadow Map Materials

A shadow Material supplies a custom shadow source.
Use the [built-in shadow baker](./HowToUse_Shadows.md) for shadows cast by scene geometry.

The shader output contains EVSM moments:

| Channel | Data |
| --- | --- |
| R | Positive warped depth. |
| G | Negative warped depth. |
| B | Square of R. |
| A | Square of G. |

The `EncodeVRCLVShadowEVSM()` helper below converts normalized radial depth to these four channels.
Define it before your source shader's fragment function:

```hlsl
float4 EncodeVRCLVShadowEVSM(float depth01) {
    float depth = saturate(depth01) * 2.0 - 1.0;
    float positive = exp(5.54 * depth);
    float negative = -exp(-5.0 * depth);
    return float4(positive, negative, positive * positive, negative * negative);
}
```

The receiver requires these moments rather than a visibility mask or raw depth.
In the fragment function, encode your radial distance from the light, including for projected Spot shadows:

```hlsl
float depth01 = (radialDistance - shadowNearClip)
    / max(shadowFarClip - shadowNearClip, 0.0001);
return EncodeVRCLVShadowEVSM(depth01);
```

Supply `radialDistance`, `shadowNearClip` and `shadowFarClip` in the same world units.
The projection, bake pose and near/far range must match the light's shadow receiver.
Convert perspective-camera depth to radial distance before this calculation.
The package's [depth encoder](../Packages/red.sim.lightvolumes/Shaders/Editor/PointLightShadowDepthEncode.shader#L57) supplies the conversion and bake-bias calculation.

Use an explicit **Far Plane** for an external source when possible.
**0 (Auto)** resolves the range from the light.
The Manager does not pass that resolved value to the source Material.

A single-view Spot source must match the Spot shadow-camera projection.
A cubemap source must supply all six matching faces.

Sources: [depth encoder](../Packages/red.sim.lightvolumes/Shaders/Editor/PointLightShadowDepthEncode.shader#L28), [shadow receiver](../Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc#L481), [Material inputs](../Packages/red.sim.lightvolumes/UScripts/LightVolumeManager.Textures.cs#L1092).

## Froxel Clustering

The shader include compiles clustering support under these conditions:

- Light support is present.
- Surface-shader analysis is inactive.
- `VRCLV_DISABLE_CLUSTERING` is absent.
- The shader target is 3.5 or higher.
- The shader API is D3D11, GLCore, Vulkan, GLES3 or Metal.

Target 3.5 uses a bit-iteration fallback.
Target 4.5 or higher can use `firstbitlow`.
The cluster-build shader targets 3.5, including Quest/GLES3.

Each cell stores a 128-bit candidate mask.
The Coarse pass finds possible lights.
The Fine pass refines that mask.
Range, Spot-cone and Area-front tests permit extra candidates.
The per-pixel light calculation then rejects those candidates with precise tests.

Use this macro to exclude clustering from one shader:

```hlsl
#define VRCLV_DISABLE_CLUSTERING 1
#include "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc"
```

Define the macro before the include.
The Manager cannot restore clustering for that shader.
The public shader API remains the same.
Keep this choice in source.
A material keyword would add unnecessary shader variants.

Sources: [capability guard](../Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc#L88), [mask iteration](../Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc#L319), [cluster construction](../Packages/red.sim.lightvolumes/Shaders/Internal/FroxelClusteringBuild.shader#L542), [camera setup](../Packages/red.sim.lightvolumes/UScripts/LightVolumeManager.Clustering.cs#L175).

## AudioLink Response

The component samples a band level and optionally smooths it.
Let `a` denote that result.
The response uses this formula:

```text
response = (Invert ? 1 - a : a) × lerp(MinimumMultiply, MaximumMultiply, a)
         + lerp(MinimumAdd, MaximumAdd, a)
```

`Invert` changes the main response term.
The Multiply and Add interpolation still uses the original `a`.
The component multiplies its selected color by the response before it calls the target light setters.

Sources: [response calculation](../Packages/red.sim.lightvolumes/Extra/Audio%20Link/LightVolumeAudioLink.cs#L124), [sample and smoothing](../Packages/red.sim.lightvolumes/Extra/Audio%20Link/LightVolumeAudioLink.cs#L210).

## Startup Shadow Bakes

**Bake In Game** adds one request when the light reaches `Start` with a Manager assigned.
An initially inactive light reaches `Start` on its first activation.
Later activations do not add another startup request.

The Manager processes at most one queued light per frame.
A projected Spot captures one view.
A Point, Area or forced-cubemap Spot captures all six views in that frame.
The queue separates lights across frames.
It does not separate faces.

Measure startup and activation frame times on the target device.
Keep the GameObject, Manager and bake dependencies available until the request completes.

Build preparation removes the Editor shadow source from the build copy.
A normal runtime bake retains a source texture and a place in the shared atlas.
Another source can cause an atlas rebuild.
The Manager can copy existing source maps into the new atlas.

This approach reduces downloaded shadow data.
It still requires runtime texture memory and bake work.

The Manager consumes a failed request without an automatic retry.
A Manager assignment after `Start` does not add a request.
`PointLightVolumeInstance.BakeShadows()` retries a bake or captures later changes.
With an active `PointLightVolumeInstance light` and [runtime bake dependencies configured](./ScriptingAPI.md#runtime-shadow-baking), call:

```csharp
light.BakeShadows();
```

Use the runtime baker's **Bake On Enable** for repeated activation bakes.

Sources: [startup request](../Packages/red.sim.lightvolumes/UScripts/PointLightVolumeInstance.cs#L313), [queue processing](../Packages/red.sim.lightvolumes/UScripts/LightVolumeManager.Buffers.cs#L142), [build preparation](../Packages/red.sim.lightvolumes/Scripts/Editor/LightVolumeBuildPreprocessor.cs#L251), [source removal](../Packages/red.sim.lightvolumes/Scripts/Editor/LightVolumeBuildPreprocessor.cs#L369), [runtime capture](../Packages/red.sim.lightvolumes/UScripts/PointLightVolumeInstance.ShadowBaking.cs#L61).

## Realtime Shadow Output

The realtime baker captures a complete shadow on each update while its target is active.
The target light supplies the resolution and blur settings.

The baker requests direct atlas output through `RuntimeShadowDirectOutput`.
Direct output requires an active, enabled light with nonzero emission.
The capture resolution must match both Manager shadow dimensions.
Otherwise, the bake retains a separate source texture.
The Manager resizes that source into the atlas when necessary.

The built-in baker owns the direct-output flag and its scratch resources.
Custom callers should use the documented [UdonSharp methods](./ScriptingAPI.md#advanced-texture-integration).

### Atlas Rebuilds

The Manager copies existing direct-output ranges into a temporary array before an atlas rebuild.
It restores each surviving light's pixels into that light's new atlas range.
A restored range must keep the same cubemap or single-slice layout.
The Manager can restore pixels even when slice IDs change or the atlas depth remains unchanged.

If the new atlas allocation fails, the Manager retains the temporary copy for an explicit retry.
If the temporary copy cannot be allocated, the rebuild stops and records an allocation failure.
Automatic updates do not repeatedly retry that failed allocation.
`LightVolumeManager.ReinitializeShadowTextures()` retries the rebuild.
With the scene's `LightVolumeManager manager`, call it after resolving the allocation problem:

```csharp
manager.ReinitializeShadowTextures();
```

A source or layout change also allows another attempt.

### A Retained Result After Realtime Stops

The baker releases its direct-output state when it stops or changes targets.
`PointLightShadowRuntimeBaker.BakeShadows()` captures a new complete shadow as a retained result.
With a configured `PointLightShadowRuntimeBaker baker`, stop continuous updates and capture once:

```csharp
baker.Realtime = false;
baker.BakeShadows();
```

This does not save the previous realtime pixels as an asset.

Sources: [baker ownership and one-shot mode](../Packages/red.sim.lightvolumes/Extra/Shadow%20Runtime%20Baker/PointLightShadowRuntimeBaker.cs#L75), [output selection](../Packages/red.sim.lightvolumes/UScripts/PointLightVolumeInstance.ShadowBaking.cs#L79), [atlas rebuild](../Packages/red.sim.lightvolumes/UScripts/LightVolumeManager.Textures.cs#L459), [direct-output preservation](../Packages/red.sim.lightvolumes/UScripts/LightVolumeManager.Textures.cs#L618).

## Baked Volume Data

Each baked volume has three 3D source textures with its L0/L1 coefficients.
The atlas generator packs them into one shared 3D atlas.
Projection sources and shadows use separate texture arrays.
The Manager publishes transforms and light settings as global shader data.
More voxels require more texture memory and bake time.
A volume transform moves its stored light field.
It does not calculate new light bounces from the surroundings.

Use [Shader Integration](./ForDevelopers.md) for the supported sample functions.
Use [Custom Lightmapper Integration](./CustomLightmapperIntegration.md#sh-layout) for the submission format.

Sources: [source texture data](../Packages/red.sim.lightvolumes/Scripts/LVUtils.cs#L286), [atlas generation](../Packages/red.sim.lightvolumes/Scripts/Editor/LightVolumeManagerEditorBackend.cs#L415), [shader textures](../Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc#L245), [shader data updates](../Packages/red.sim.lightvolumes/UScripts/LightVolumeManager.Buffers.cs#L692).
