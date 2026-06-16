[VRC Light Volumes](../README.md) | **How to Use** | [Best Practices](../Documentation/BestPractices.md) | [Udon Sharp API](../Documentation/UdonSharpAPI.md) | [For Shader Developers](../Documentation/ForShaderDevelopers.md) | [Compatible Shaders](../Documentation/CompatibleShaders.md)

# How to Use

| Menu |
|----|
|[VRC Light Volumes System](../Documentation/HowToUse.md)|
|[Regular Light Volumes](../Documentation/HowToUse_RegularLightVolumes.md)|
|[Point Light Volumes](../Documentation/HowToUse_PointLightVolumes.md)|
|**Point Light Volume Shadows**<br />• [Shadow Types](#Shadow-Types)<br />• [Baked Shadows Setup](#Baked-Shadows-Setup)<br />• [Realtime Shadow Baker](#Realtime-Shadow-Baker)<br />• [Runtime Script Control](#Runtime-Script-Control)<br />• [Performance Notes](#Performance-Notes)<br />• [Shadow Parameters](#Shadow-Parameters)|
|[Audio Link Integration](../Documentation/HowToUse_AudioLinkIntegration.md)|
|[TV Screens Integration](../Documentation/HowToUse_TVScreensIntegration.md)|
|[How Light Volumes Work?](../Documentation/HowToUse_HowItWorks.md)|

## Point Light Volume Shadows

**Point Light Volumes** can use shadow maps. They are not stored inside regular Light Volumes. Instead, every shadow source is packed into a shared EVSM shadow texture array and sampled by shaders that support VRC Light Volumes.

![](../Documentation/Preview_9.png)

Shadows are available for Point, Spot and Area Light Volumes. They affect VRC Light Volumes compatible shaders, but the shader must pass `World Normal` into the Light Volumes functions to evaluate shadowing and normal masking. Default Unity shaders will not show Point Light Volumes or their shadows.

## Shadow Types

### Baked

Baked shadow maps are generated in the Unity Editor and saved as assets. Use them for static or mostly static lights. They do not render shadow cameras in runtime, so they are much cheaper than realtime shadow baking, but they are still more expensive than the same Point Light Volume without shadows because the shader needs extra shadow data and the world needs extra shadow texture memory.

Even baked Point Light Volume shadows are sampled at runtime by compatible shaders, so they can still shade moving objects, avatars and props that use VRC Light Volumes shader integration.

### Runtime Updated Texture Source

The `Shadow Map` field can accept a Cubemap, Texture2DArray, RenderTexture, or Material. RenderTextures and Materials can be copied into the shared shadow texture array at runtime when `Auto Update Textures` is enabled in **Light Volume Setup**. In the editor, Point Light Volume automatically marks RenderTexture and Material shadow sources for auto-update during Udon sync.

This mode is useful when another system already produces a shadow-like texture. If you only need static shadows, use editor baking instead.

### Realtime Shadow Baker

The extra **Realtime Shadow Baker** component, `PointLightShadowRuntimeBaker`, renders shadow maps in runtime for a selected **Point Light Volume Instance**.

This is the most expensive shadow mode. It renders one or more shadow cameras, encodes EVSM depth, can run blur passes, and then writes the result into the shared shadow texture array. In practice this is more expensive than regular Unity realtime shadows, so use it only for a small number of important lights.

## Baked Shadows Setup

1. Select a **Point Light Volume**.
2. Enable `Shadows`.
3. Choose the light `Type`.
   Point Lights and Area Lights use cubemap shadows. Spot Lights use a single projected shadow texture when `Angle` is below 180 degrees, unless `Force Cubemap Shadows` is enabled.
4. Configure `Layer Mask`, `Object Mask`, `Near Plane`, `Bias`, `Blur` and `Contact Hardening`.
5. Press `Bake Shadows` in the **Point Light Volume** inspector.
6. For batch baking, enable `Rebake Shadows` on the lights you want to update, then press `Bake Shadows` in **Light Volume Setup**.

`Shadow Resolution` and `Shadow Texture Format` are configured in **Light Volume Setup**. `Half` format is cheaper, while `Float` can reduce EVSM precision artifacts. Higher resolution improves detail but increases VRAM usage, especially for Point and Area Lights because cubemap shadows use 6 texture array slices.

`Use World Space` keeps the baked shadow projection fixed in world space instead of moving it with the light. This is useful for a light that changes color or intensity but should keep shadows attached to the room. It is less optimized than local-space shadows.

> [!IMPORTANT]
> Shadow map baking doesn't support transparent or semi-transparent occluders correctly. If you have meshes like glass, water or foliage, disable them or move them to a layer excluded by `Layer Mask` while baking shadows.

## Realtime Shadow Baker

Use the extra `PointLightShadowRuntimeBaker` component when a light needs to cast shadows from moving objects in runtime.

1. Add `PointLightShadowRuntimeBaker` from `Packages/red.sim.lightvolumes/Extra/Shadow Runtime Baker` to a GameObject.
2. Assign `Target Point Light Volume` to the target **Point Light Volume Instance**.
3. Make sure the target Point Light Volume has `Shadows` enabled and its shadow settings configured.
4. Set `Resolution`.
   For the cheapest realtime path, keep this equal to `Shadow Resolution` in **Light Volume Setup**. If the values are different, the baker still works, but it has to use a local RenderTexture and copy slices into the manager shadow array.
5. Enable `Bake On Enable` if the shadow should be baked once when the component becomes active.
6. Enable `Realtime` only when the shadow needs to keep updating.
7. Adjust `Realtime Faces Per Frame`.
   This only affects cubemap shadows. `1` spreads a full cubemap update across 6 frames, while `6` updates all faces in one bake tick. Single-slice Spot Light shadows always update one slice.
8. Keep `Shadow Blur Sample Preset` as low as possible for the result you need.

The baker uses the target Point Light Volume shadow settings, including `Layer Mask`, `Near Plane`, `Bias`, `Blur`, `Contact Hardening` and the light culling range. Configure those on the target light before relying on runtime baking.

The hidden shadow camera and runtime materials are prepared automatically by the editor and build preprocessor. You don't need to assign them manually.

### Single-Slice Realtime Spot Shadows

Single-slice Spot Light shadows are the cheapest realtime option.

To use them:

1. Set the light `Type` to `Spot Light`.
2. Keep `Angle` below 180 degrees.
3. Keep `Force Cubemap Shadows` disabled.
4. If this light previously used a cubemap shadow source, press `Bake Shadows` once or clear the old cubemap source after changing the settings so the target instance is synced as a single projected shadow.
5. Use the Realtime Shadow Baker with `Realtime` enabled.

This renders one projected shadow slice instead of a 6-face cubemap, so it is the best choice for flashlights, projectors, small stage lights, or other lights that only need to look in one direction.

## Runtime Script Control

The runtime baker can be triggered from UdonSharp by calling `BakeShadows()`. If `Realtime` is enabled, this also starts the realtime bake loop.

```csharp
using UdonSharp;
using UnityEngine;
using VRCLightVolumes;

public class ShadowRefreshButton : UdonSharpBehaviour {
    public PointLightShadowRuntimeBaker ShadowBaker;

    public void RefreshShadow() {
        ShadowBaker.BakeShadows();
    }
}
```

If you turn realtime mode on from script while the baker is already enabled, call `BakeShadows()` after changing `Realtime` so the loop starts immediately.

```csharp
public void EnableRealtimeShadows() {
    ShadowBaker.Realtime = true;
    ShadowBaker.enabled = true;
    ShadowBaker.BakeShadows();
}

public void DisableRealtimeShadows() {
    ShadowBaker.Realtime = false;
    ShadowBaker.enabled = false;
}
```

If you replace a Point Light Volume shadow source manually from Udon, rebuild the shadow texture cache through the manager after changing the source. Use `NotifyPointLightVolumeChanged(pointLight, true, false, true)` when changing one light, or `ReinitializeShadowTextures()` after broader manual changes.

If you change `AutoUpdateShadowMap` from Udon, the manager's `AutoUpdateTextures` must also be enabled for the source to update automatically.

Do not call shadow cache rebuild methods every frame. For per-frame shadows, use `PointLightShadowRuntimeBaker` with `Realtime` enabled, or use `Auto Update Textures` only for RenderTexture or Material sources that really need continuous updates.

## Performance Notes

No shadows is always the cheapest mode.

Baked shadows are usually reasonable for static lights. They cost extra VRAM and extra shader sampling, but they do not render cameras in runtime. They are still more expensive than the same light without shadows.

Realtime Shadow Baker is expensive. It renders shadow camera views in runtime, encodes the depth into EVSM data, optionally blurs it, and updates the shared shadow array. It is more expensive than Unity realtime shadows, so avoid using it on many lights at the same time.

For realtime shadows:

- Prefer single-slice Spot Light shadows when possible.
- Use the lowest `Resolution` that still looks acceptable.
- Keep `Shadow Blur Sample Preset` low. It only matters when `Blur` is above `0`.
- Keep `Contact Hardening` at `0` unless you really need it.
- Use `Layer Mask` and `Object Mask` to render only actual shadow casters.
- Keep `Realtime Faces Per Frame` low for cubemap shadows unless you need all faces updated immediately.
- Prefer baked shadows when the light or its shadow casters are static.

## Shadow Parameters

| Parameter | Description |
| --- | --- |
|`Shadows` | Enables shadow map sampling for this Point Light Volume. Requires a baked, assigned, auto-updated, or runtime-baked shadow source.|
|`Shadow Map` | Shadow texture source used by this light. Can be generated by `Bake Shadows`, assigned manually, or updated by the runtime baker. Supports Cubemap, Texture2DArray, RenderTexture and Material.|
|`Layer Mask` | Layers that can cast shadows during editor or runtime shadow baking.|
|`Object Mask` | Optional object list. If empty, all objects on the selected layers can cast shadows. If not empty, only children of the listed objects are rendered during the bake.|
|`Near Plane` | Near clip plane used by the shadow bake camera. Higher values can clip nearby occluders.|
|`Bias` | World-space bias in meters used while baking shadows. Larger values reduce self-shadow artifacts but can detach contact edges.|
|`Blur` | Gaussian blur radius applied after baking, normalized to 128x128 shadow resolution. `0` keeps shadows unblurred.|
|`Contact Hardening` | Hardens shadows near contact areas. It can produce artifacts and is more expensive in runtime shadow mode.|
|`Use World Space` | Keeps baked shadows attached to the baked world-space pose instead of moving them with the light. Less optimized when enabled.|
|`Force Cubemap Shadows` | Forces Spot Light shadows to bake and store as a cubemap even when the spot angle could use a single projected shadow texture.|
|`Rebake Shadows` | Includes this light when pressing `Bake Shadows` in **Light Volume Setup**.|
|`Shadow Resolution` | Resolution used by the shared shadow texture array. For cubemap shadows this value is per face.|
|`Shadow Texture Format` | Precision used by baked EVSM shadow maps and the runtime shadow texture array. `Half` is cheaper, `Float` reduces EVSM precision artifacts.|
|`Bake On Enable` | Realtime Shadow Baker option. Runs one distributed bake cycle when the baker becomes active.|
|`Realtime` | Realtime Shadow Baker option. Continuously updates shadow slices through a delayed Udon event loop.|
|`Realtime Faces Per Frame` | Realtime Shadow Baker option. Number of cubemap faces updated per bake tick. Single-slice Spot Light shadows ignore this and update one slice.|
|`Shadow Blur Sample Preset` | Realtime Shadow Baker option. Controls runtime blur sample count. Lower presets are cheaper.|
