[VRC Light Volumes](../README.md) | **How to Use** | [Best Practices](../Documentation/BestPractices.md) | [Udon Sharp API](../Documentation/UdonSharpAPI.md) | [For Shader Developers](../Documentation/ForShaderDevelopers.md) | [Compatible Shaders](../Documentation/CompatibleShaders.md)

# How to Use

| Menu |
|----|
|[VRC Light Volumes System](../Documentation/HowToUse.md)|
|[Regular Light Volumes](../Documentation/HowToUse_RegularLightVolumes.md)|
|[Point Light Volumes](../Documentation/HowToUse_PointLightVolumes.md)|
|**Point Light Volume Shadows**<br />• [Shadow Types](#Shadow-Types)<br />• [Baked Shadows Setup](#Baked-Shadows-Setup)<br />• [Shadow Stability Tuning](#Shadow-Stability-Tuning)<br />• [Realtime Shadow Baker](#Realtime-Shadow-Baker)<br />• [Runtime Blur Modes](#Runtime-Blur-Modes)<br />• [Runtime Script Control](#Runtime-Script-Control)<br />• [Performance Notes](#Performance-Notes)<br />• [Shadow Parameters](#Shadow-Parameters)|
|[Area Light Emission](../Documentation/HowToUse_AreaLightEmission.md)|
|[Audio Link Integration](../Documentation/HowToUse_AudioLinkIntegration.md)|
|[TV Screens Integration](../Documentation/HowToUse_TVScreensIntegration.md)|
|[How Light Volumes Work?](../Documentation/HowToUse_HowItWorks.md)|

## Point Light Volume Shadows

**Point Light Volumes** can use shadow maps. Under the hood, every shadow map is encoded as Exponential Variance Shadow Map (EVSM) moments, packed into a shared shadow texture array, and sampled by shaders that support VRC Light Volumes. EVSM stores positive and negative warped depth moments, so the shared array uses four channels with `Half` precision on Android/Quest/iOS and `Float` precision on PC.

![](../Documentation/Preview_9.png)

Shadows are available for Point, Spot and Area Light Volumes. They affect VRC Light Volumes v.3.0 compatible shaders. Older VRC Light Volumes (v.2.x.x) shaders will work overall, but will not draw shadows. Default Unity shaders will not show Point Light Volumes or their shadows.

## Shadow Types

### Baked Realtime

Baked shadow maps are generated in the Unity Editor and saved as assets. Use them for static or mostly static lights. They do not render shadow maps in runtime, so they are much cheaper than realtime shadow baking. But they still affect all the dynamic objects with materials that supports VRC Light Volumes. Howewer no dynamic objects will cast shadows in runtime. So technically it's realtime shadows with no runtime baking.
But they are still more expensive than the same Point Light Volume without shadows because the shader needs extra shadow data and extra VRAM.

Even baked Point Light Volume shadows are sampled at runtime by compatible shaders, so they can still shade moving objects, avatars and props that use VRC Light Volumes shader integration.

Editor-baked shadow blur always uses the spherical shadow-space blur path. This keeps cubemap face edges and single-slice Spot Light shadow edges more consistent, especially with larger `Blur` values.

### Realtime Shadow Baker

The extra **Point Light Shadow Runtime Baker** component, renders shadow maps in runtime for a selected **Point Light Volume Instance**.

This is the most expensive shadow mode. It renders one or more shadow cameras, encodes EVSM moments, can run blur passes, and then writes the result into the shared shadow texture array. In practice this is more expensive than regular Unity realtime shadows, so use it only for a small number of important lights.

### Runtime Updated Texture Source

It's a highly advanced feature and not something you really need unless you know what you're doing! 
The `Shadow Map` field can accept a Cubemap, Texture2DArray, RenderTexture, or Material. RenderTextures and Materials can be copied into the shared shadow texture array at runtime when `Auto Update Textures` is enabled in **Light Volume Setup**. In the editor, Point Light Volume automatically marks RenderTexture and Material shadow sources for auto-update during Udon sync.
This mode is useful when another system already produces a shadow-like texture. If you only need static shadows, use editor baking instead.

## Baked Shadows Setup

1. Enable `Shadows` flag in your **Point Light Volume**.
2. Configure the `Layer Mask`. Select only layers you want to bake shadows from.
3. Configure the `Blur` value to control the shadows penumbra.
4. Press `Bake Shadows` in the **Point Light Volume** inspector.

Optional:
Configure `Object Mask` if needed. If not empty, only children of the listed objects are rendered during the bake.
Increase `Near Plane` value if you want to clip the meshes near the light source.
Keep `Far Clip Plane` at `0` in most cases. `0` automatically uses the current calculated culling range of the light, which is usually the correct distance and avoids clipping valid shadow casters. Set a manual value only when you intentionally want to limit how far the shadow bake camera can see.
Configure `Bias` if you have visible self-shadow artifacts.
Use `Contact Hardening` if you want to increase shadow sharpness near the shadow casters. However it can cause visible artefacts, so use it carefully!
`Use World Space` keeps the baked shadow projection fixed in world space instead of moving it with the light. This is useful for a light that changes color or intensity but should keep shadows attached to the room. It is less optimized than local-space shadows.
`Shadow Resolution` is configured in **Light Volume Setup**. Shadow precision is selected automatically from the active build target: Android/Quest/iOS uses `Half`, while PC uses `Float`. Higher resolution improves detail but increases VRAM usage, especially for Point and Area Lights because cubemap shadows use 6 texture array slices.

Changing the active Unity build target forces shadow rebaking for lights marked with `Rebake Shadows`, because Half and Float baked assets use different texture formats.

## Shadow Stability Tuning

EVSM shadows are cheap and filter well, but they can show light bleeding or noisy bright edges, especially with `Half` precision on Quest and Mobile. The global correction controls are in **Light Volume Setup** and affect all Point Light Volume shadows:

- `Shadow Bleed Reduction` suppresses EVSM light bleeding by remapping shadow visibility. Increase it when shadow edges leak too much light or Half precision shows bright edge noise. Higher values can collapse soft penumbra and visually eat the shadow, so compensate with a little more per-light `Blur` when needed.
- `Shadow Min Variance` clamps the minimum EVSM variance used by the receiver shader. The Setup inspector exposes it as a human-readable `0..1` slider, mapped logarithmically to the raw shader range `0.0001..1.0`. Higher values reduce Half precision edge noise but can detach contact shadows and reduce contact darkness, so use the smallest value that fixes the artifact.

Practical workflow:

1. Switch the Unity build target to Android/Quest/iOS or PC first, so **Light Volume Setup** can select the matching shadow precision and rebake if needed.
2. Keep per-light `Bias` only high enough to hide self-shadow acne. Bias is not the right tool for EVSM light bleeding and can detach contact shadows quickly.
3. Raise `Shadow Bleed Reduction` gradually until obvious leaking disappears.
4. If Half shadows still have noisy bright rims, raise `Shadow Min Variance` slightly.
5. If the penumbra becomes too thin after bleed reduction or variance changes, increase the affected light's `Blur`.

`Near Plane` and `Far Clip Plane` also affect precision. Shadow depth is normalized between them, so moving `Near Plane` farther from the light or manually reducing `Far Clip Plane` can improve usable depth precision. Do not push `Near Plane` past real shadow casters, and do not pull `Far Clip Plane` closer than objects that should cast shadows. For most lights, leave `Far Clip Plane` at `0`; the system will recalculate it from the light's current culling range. Changing `Near Plane`, `Far Clip Plane`, `Bias`, `Blur` or `Contact Hardening` requires rebaking the affected shadow.

## Realtime Shadow Baker

Use the extra `Point Light Shadow Runtime Baker` component when a light needs to cast shadows from moving objects in runtime.

1. Add `Point Light Shadow Runtime Baker` component to a GameObject.
2. Assign `Target Point Light Volume` to the target **Point Light Volume Instance**.
3. Make sure the target Point Light Volume has `Shadows` enabled and its shadow settings configured.
4. Set `Resolution`.
> [!NOTE]
> For the best result, keep `Resolution` equal to `Shadow Resolution` in **Light Volume Setup**. However, you can lower the resolution here if you want to have a lower resolution for some of the light shadows.
5. Enable `Bake On Enable` if the shadow should be baked once when the component becomes active.
6. Enable `Realtime` only when the shadow needs to keep updating every frame. Basically this makes the shadows fully realtime.
7. Adjust `Realtime Faces Per Frame`. This only affects cubemap shadows. `1` spreads a full cubemap update across 6 frames, while `6` updates all faces in one bake tick. Single-slice Spot Light shadows always update one slice.
> [!NOTE]
> You usually want to have it as `1` if it's a static light that bakes once at the start. And you usually want to have it as `6` if it's a dynamic light with fully realtime shadows. 
8. Keep `Shadow Blur Sample Preset` as low as possible for the result you need. Lowering the blur quality improves GPU performance. However, with realtime shadows, sometimes the bottleneck is on CPU side, so it might cause no actual effect. But still can be very noticable on **Quest** and **Mobile**!
9. Enable `Spherical Blur` only when `Planar Blur` shows visible cubemap seams or Spot Light projection-edge artifacts. It reduces those artifacts, but costs more GPU work.

> [!IMPORTANT]
> Realtime shadows will only be visible in **Play Mode** and in **VRChat**. Scene view realtime shadows are not supported yet!

The baker uses the target Point Light Volume shadow settings, including `Layer Mask`, `Near Plane`, `Far Clip Plane`, `Bias`, `Blur`, `Contact Hardening` and the light culling range. Configure those on the target light before relying on runtime baking. `Far Clip Plane = 0` is also the normal default for runtime baking; it recalculates the far clip from the light's current culling range before rendering.
**Blur** value `0` completely turns off the blur improving the performance on GPU side, but makes the quality worse, so it's not recommended. 
**Contact Hardening** value `0` completely turns off the contact hardening effect improving the performance on GPU side. It's recommended to keep it `0` in most scenarios, because it can cause artefacts.
When `Spherical Blur` is enabled, runtime `Blur` and `Contact Hardening` both sample in spherical shadow space instead of planar texture space.

> [!TIP]
> You can leave point light with no shadows baked at all, but keep the `Point Light Shadow Runtime Baker` with `Bake On Enable` option only. It will bake shadows on start in runtime, and you'll save the asset bundle memory. Especially useful for **Quest** and **Mobile** where VRChat limits the world asset bundle to 100mb. 

### Runtime Blur Modes

Runtime shadows have two blur modes:

- `Planar Blur` is the cheaper default runtime path. It uses two texture-space blur passes and `Shadow Blur Sample Preset` maps to 30/62/126 total blur taps for Low/Medium/High.
- `Spherical Blur` is the more geometrically correct runtime path. It samples a one-pass radial kernel in spherical shadow space, so cubemap face seams and single-slice Spot Light edge divergence are much less visible. Its blur presets use 33/65/129 taps for Low/Medium/High.

Under the hood, `Planar Blur` treats each shadow slice or cubemap face as a flat texture. It blurs in texture space, first in one axis and then in the other axis. This is fast, but the blur kernel does not follow the real cubemap or Spot Light projection shape. Near cubemap face edges and wide Spot Light projection edges, the apparent blur radius can diverge and make seams more visible.

`Spherical Blur` offsets samples in shadow direction space instead. For cubemap shadows it can cross face edges consistently, and for single-slice Spot Light shadows it keeps the blur closer to a stable angular radius. This reduces visible seams and projection-edge artifacts, but each tap needs extra shadow-space reprojection, so it is more expensive than `Planar Blur`.

Editor `Bake Shadows` always uses spherical shadow-space blur. The runtime option exists only so you can choose the cost/artifact tradeoff for realtime baking.

### Single-Slice Realtime Spot Shadows

Single-slice Spot Light shadows are the cheapest realtime option. It's 6 times cheaper than **Area Lights** and **Point Lights** realtime shadows.
It's the best choice for flashlights, projectors, small stage lights, or other lights that only need to look in one direction.

To use them:

1. Set the light `Type` to `Spot Light`.
2. Keep `Angle` below 180 degrees. (I personally recommend a value even below 120 degrees to keep the quality decent)
3. Keep `Force Cubemap Shadows` disabled.
4. If this light previously used a cubemap shadow source, delete the baked texture reference in the component.
5. Use the Realtime Shadow Baker with `Realtime` enabled.

## Runtime Script Control

The runtime baker can be triggered from UdonSharp by calling `BakeShadows()`. If `Realtime` is enabled, this also starts the realtime bake loop.
If you turn realtime mode on from script while the baker is already enabled, call `BakeShadows()` after changing `Realtime` so the loop starts immediately.

If you replace a Point Light Volume shadow source manually from Udon, rebuild the shadow texture cache through the manager after changing the source. Use `NotifyPointLightVolumeChanged()` when changing one light, or `ReinitializeShadowTextures()` after several changes.

If you only change shadow ID, world-space shadow mode, near clip or runtime bake settings from Udon, use `PointLightVolumeInstance.SetShadowSettings()` so the instance stores the new values and updates the manager only when shader-facing shadow data actually changes.

If you change `AutoUpdateShadowMap` from Udon, the manager's `AutoUpdateTextures` must also be enabled for the source to update automatically.

Do not call shadow cache rebuild methods every frame. For per-frame shadows, use `PointLightShadowRuntimeBaker` with `Realtime` enabled, or use `Auto Update Textures` only for RenderTexture or Material sources that really need continuous updates.

## Performance Notes

No shadows is always the cheapest mode.

Baked shadows are usually reasonable for static lights. They cost extra VRAM and extra shader sampling, but they do not render shadow maps in runtime. They are still more expensive than the same light without shadows.

Realtime Shadow Baker is very expensive. It renders shadow camera views in runtime, encodes the depth into EVSM moments, optionally blurs it, and updates the shared shadow array. It is more expensive than Unity realtime shadows, so avoid using it on many lights at the same time.

For realtime shadows:

- Prefer single-slice Spot Light shadows when possible.
- Use the lowest `Resolution` that still looks acceptable.
- Keep `Shadow Blur Sample Preset` low. It only matters when `Blur` is above `0`.
- Keep `Spherical Blur` disabled unless `Planar Blur` produces visible cubemap seams or Spot Light projection-edge artifacts.
- Keep `Contact Hardening` at `0` unless you really need it.
- Use `Layer Mask` and `Object Mask` to render only actual shadow casters.
- Keep `Realtime Faces Per Frame` low for cubemap shadows unless you need all faces updated immediately.

## Shadow Parameters

| Parameter | Description |
| --- | --- |
|`Shadows` | Enables shadow map sampling for this Point Light Volume. Requires a baked, assigned, auto-updated, or runtime-baked shadow source.|
|`Shadow Map` | Shadow texture source used by this light. Can be generated by `Bake Shadows`, assigned manually, or updated by the runtime baker. Supports Cubemap, Texture2DArray, RenderTexture and Material.|
|`Layer Mask` | Layers that can cast shadows during editor or runtime shadow baking.|
|`Object Mask` | Optional object list. If empty, all objects on the selected layers can cast shadows. If not empty, only children of the listed objects are rendered during the bake.|
|`Near Plane` | Near clip plane used by the shadow bake camera. Shadow depth is normalized between `Near Plane` and `Far Clip`, so higher values can improve precision but can clip nearby occluders.|
|`Far Clip Plane` | Far clip plane used by the shadow bake camera. `0` recalculates it from the light's current culling range and is usually the recommended default. Set it manually only to clip distant shadow casters, reduce wasted depth range, or stabilize precision for a known bounded shadow area. Too small values clip valid shadow casters.|
|`Bias` | World-space bias in meters used while baking shadows. Larger values reduce self-shadow artifacts but can detach contact edges.|
|`Blur` | Shadow blur radius applied after baking, normalized to 128x128 shadow resolution. Editor baking uses spherical shadow-space blur to reduce visible cubemap and Spot Light projection seams. Runtime baking uses `Planar Blur` unless `Spherical Blur` is enabled. `0` keeps shadows unblurred.|
|`Contact Hardening` | Hardens shadows near contact areas. It can produce artifacts and is more expensive in runtime shadow mode. When `Spherical Blur` is enabled on the runtime baker, contact hardening samples use the same spherical shadow-space kernel.|
|`Use World Space` | Keeps baked shadows attached to the baked world-space pose instead of moving them with the light. Less optimized when enabled.|
|`Force Cubemap Shadows` | Forces Spot Light shadows to bake and store as a cubemap even when the spot angle could use a single projected shadow texture.|
|`Rebake Shadows` | Includes this light when pressing `Bake Shadows` in **Light Volume Setup**.|
|`Shadow Resolution` | Resolution used by the shared shadow texture array. For cubemap shadows this value is per face.|
|`Shadow Format` | Read-only precision selected automatically by the active build target. Android/Quest/iOS uses `Half`; PC uses `Float`.|
|`Shadow Bleed Reduction` | Global EVSM light bleeding correction. Higher values reduce leaking and some Half edge noise, but can collapse soft penumbra.|
|`Shadow Min Variance` | Global minimum EVSM variance clamp. In **Light Volume Setup** this is a `0..1` logarithmic slider over the raw `0.0001..1.0` range. Higher values reduce Half precision edge noise but can detach contact shadows.|
|`Bake On Enable` | Realtime Shadow Baker option. Runs one distributed bake cycle when the baker becomes active.|
|`Realtime` | Realtime Shadow Baker option. Continuously updates shadow slices through a delayed Udon event loop.|
|`Realtime Faces Per Frame` | Realtime Shadow Baker option. Number of cubemap faces updated per bake tick. Single-slice Spot Light shadows ignore this and update one slice.|
|`Shadow Blur Sample Preset` | Realtime Shadow Baker option. Controls runtime blur and contact hardening sample count. Lower presets are cheaper. `Planar Blur` uses 30/62/126 two-pass blur taps; `Spherical Blur` uses 33/65/129 one-pass blur taps.|
|`Spherical Blur` | Realtime Shadow Baker option. Samples runtime blur and contact hardening in spherical shadow space to reduce visible cubemap and single-slice Spot Light projection seams. More correct, but more expensive than `Planar Blur`.|
