[VRC Light Volumes](../README.md) | **How to Use** | [Best Practices](../Documentation/BestPractices.md) | [Udon Sharp API](../Documentation/UdonSharpAPI.md) | [For Shader Developers](../Documentation/ForShaderDevelopers.md) | [Compatible Shaders](../Documentation/CompatibleShaders.md)

# How to Use

| Menu |
|----|
|[VRC Light Volumes System](../Documentation/HowToUse.md)|
|[Regular Light Volumes](../Documentation/HowToUse_RegularLightVolumes.md)|
|[Point Light Volumes](../Documentation/HowToUse_PointLightVolumes.md)|
|**Point Light Volume Shadows**<br />- [Shadow Types](#Shadow-Types)<br />- [Baked Shadows Setup](#Baked-Shadows-Setup)<br />- [Bake In Game](#Bake-In-Game)<br />- [Shadow Stability Tuning](#Shadow-Stability-Tuning)<br />- [Realtime Shadow Baker](#Realtime-Shadow-Baker)<br />- [Runtime Blur Modes](#Runtime-Blur-Modes)<br />- [Runtime Script Control](#Runtime-Script-Control)<br />- [Performance Notes](#Performance-Notes)<br />- [Shadow Parameters](#Shadow-Parameters)|
|[Point Light Material Sources](../Documentation/HowToUse_PointLightMaterialSources.md)|
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

Baked shadow maps are generated in the Unity Editor and saved as assets. Use them for static or mostly static lights. They do not render shadow maps in runtime, so they are much cheaper than realtime shadow baking. They still affect moving objects, avatars and props that use VRC Light Volumes shader integration, but moving objects do not cast new shadows unless you rebake in runtime.

Even baked Point Light Volume shadows are more expensive than the same Point Light Volume without shadows because the shader needs extra shadow data and extra VRAM.

Editor-baked shadow blur always uses the spherical shadow-space blur path. This keeps cubemap face edges and single-slice Spot Light shadow edges more consistent, especially with larger `Blur` values.

### Bake In Game

`Bake In Game` bakes this Point Light Volume shadow once from `Start()` in Play Mode or VRChat. You do not need to add the extra runtime baker component for this one-shot startup bake.

In the Unity Editor, the light can still use an assigned editor-baked shadow texture for previewing and authoring. When building or uploading, the build preprocessor removes that baked shadow texture reference from `Bake In Game` lights, so the texture does not enter the build or asset bundle. The light then bakes its shadow in runtime on start, saving bundle memory.

`Bake In Game` uses the highest runtime shadow bake quality, spherical blur, the configured runtime/setup resolution and a one-frame bake. It is intended for static or mostly static shadow casters where startup cost is acceptable and bundle memory is more important than avoiding runtime work.

### Realtime Shadow Baker

The extra **Point Light Shadow Runtime Baker** component is an extension for lights that need a rebake on `OnEnable` or full realtime shadow updates. It does not contain the shadow rendering pipeline itself anymore; it configures the target **Point Light Volume Instance** and triggers its native runtime bake.

Full realtime mode is the most expensive shadow mode. It renders shadow camera views in runtime, encodes EVSM moments, can run blur passes, and then writes the result into the shared shadow texture array every update. In practice this is more expensive than regular Unity realtime shadows. Use it only for a small number of heroic lights, flashlights, or other single important lights.

Prefer Spot Lights for realtime shadow baking. With `Force Cubemap Shadows` disabled and an angle below 180 degrees, a Spot Light uses one shadow slice and is about six times cheaper than Point Light or Area Light cubemap shadows. For quality, keep Spot Light angles around 120 degrees or lower when possible. Point Light realtime shadows are very expensive.

### Runtime Updated Texture Source

It's a highly advanced feature and not something you really need unless you know what you're doing!
The `Shadow Map` field can accept a Cubemap, Texture2DArray, RenderTexture, or Material. RenderTextures and Materials can be copied into the shared shadow texture array at runtime when `Auto Update Textures` is enabled in **Light Volume Setup**. In the editor, Point Light Volume automatically marks RenderTexture and Material shadow sources for auto-update during Udon sync.
This mode is useful when another system already produces a shadow-like texture. If you only need static shadows, use editor baking or `Bake In Game` instead.

Material shadow sources must output VRC Light Volumes EVSM moments, not a regular visibility mask. See [Point Light Material Sources](../Documentation/HowToUse_PointLightMaterialSources.md) for the required channel layout, cubemap face order and single-slice projection rules.

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
Use `Contact Hardening` if you want to increase shadow sharpness near the shadow casters. However it can cause visible artifacts, so use it carefully.
`Use World Space` keeps the baked shadow projection fixed in world space instead of moving it with the light. This is useful for a light that changes color or intensity but should keep shadows attached to the room. It is less optimized than local-space shadows.
`Shadow Resolution` is configured in **Light Volume Setup**. Shadow precision is selected automatically from the active build target: Android/Quest/iOS uses `Half`, while PC uses `Float`. Higher resolution improves detail but increases VRAM usage, especially for Point and Area Lights because cubemap shadows use 6 texture array slices.

Changing the active Unity build target forces shadow rebaking for lights marked with `Rebake Shadows`, because Half and Float baked assets use different texture formats.

## Bake In Game

Use `Bake In Game` when you want a light to have a shadow in VRChat, but you do not want to ship a baked shadow texture in the build or asset bundle.

1. Enable `Shadows` on the **Point Light Volume**.
2. Configure the normal shadow bake settings: `Layer Mask`, optional `Object Mask`, `Near Plane`, `Far Clip Plane`, `Bias`, `Blur`, `Contact Hardening`, and `Force Cubemap Shadows`.
3. Enable `Bake In Game` on the **Point Light Volume**.
4. Optionally press `Bake Shadows` in the editor if you want an editor preview. This preview texture can stay assigned while authoring.

When `Bake In Game` is enabled, the build preprocessor treats the Udon instance as the source of truth for runtime. It clears the editor-baked `Shadow Map` source from the temporary build/upload scene, prepares the shared runtime shadow camera and materials on the manager, and lets the light bake itself once from `Start()`.

`Bake In Game` does not rebake every time the light is enabled. It is a one-shot startup bake. If you need a rebake each time an object is enabled, use **Point Light Shadow Runtime Baker** with `Bake On Enable`.

## Shadow Stability Tuning

EVSM shadows are cheap and filter well, but `Half` precision on Quest and Mobile can show noisy or glitchy bright artifacts. These artifacts usually appear on shadow edges, in mesh corners, and near the first contact area where the shadow starts next to the occluder. The global correction controls are in **Light Volume Setup** and affect all Point Light Volume shadows:

- `Shadow Min Variance` clamps the minimum EVSM variance used by the receiver shader. The Setup inspector exposes it as a human-readable `0..1` slider, mapped logarithmically to the raw shader range `0.0001..1.0`. This value is stored separately for PC and Android/Quest/iOS. PC usually needs a much lower value because it uses `Float` shadow textures, while Quest and Mobile often need a higher value because they use `Half`. Higher values reduce Half precision edge, corner and contact-start noise, but can detach contact shadows and reduce contact darkness, so use the smallest value that fixes the artifact on the target platform.
- `Shadow Bleed Reduction` remaps shadow visibility and helps suppress remaining bright edge artifacts after variance tuning. It also reduces classic EVSM light bleeding, but higher values can collapse soft penumbra and visually eat the shadow, so compensate with a little more per-light `Blur` when needed.

Practical workflow:

1. Switch the Unity build target to Android/Quest/iOS or PC first, so **Light Volume Setup** can show the matching `Shadow Min Variance`, select the matching shadow precision and rebake if needed.
2. Keep per-light `Bias` only high enough to hide self-shadow acne. Bias is not the right tool for Half precision edge and corner artifacts, and it can detach contact shadows quickly.
3. On Quest and Mobile, raise the Android/Quest/iOS `Shadow Min Variance` first. A value of `1` is a normal starting point for Quest.
4. If bright edge speckles, corner glitches or small halo artifacts remain, raise `Shadow Bleed Reduction` gradually.
5. If the penumbra becomes too thin after bleed reduction or variance changes, increase the affected light's `Blur`.

For Quest and Mobile, a practical baseline is `Shadow Min Variance = 1` and `Shadow Bleed Reduction` around `0.2..0.4`. PC has a separate `Shadow Min Variance` value, so you can keep the PC value low for cleaner contact shadows while using a stronger mobile value to hide Half precision edge, corner and contact-start artifacts.

`Near Plane` and `Far Clip Plane` also affect precision. Shadow depth is normalized between them, so moving `Near Plane` farther from the light or manually reducing `Far Clip Plane` can improve usable depth precision. Do not push `Near Plane` past real shadow casters, and do not pull `Far Clip Plane` closer than objects that should cast shadows. For most lights, leave `Far Clip Plane` at `0`; the system will recalculate it from the light's current culling range. Changing `Near Plane`, `Far Clip Plane`, `Bias`, `Blur` or `Contact Hardening` requires rebaking the affected shadow.

## Realtime Shadow Baker

Use the extra `Point Light Shadow Runtime Baker` component only when a light needs `OnEnable` rebaking or full realtime shadows from moving runtime casters. For simple startup baking, use the Point Light Volume `Bake In Game` checkbox instead.

1. Add `Point Light Shadow Runtime Baker` component to a GameObject.
2. Assign `Target Point Light Volume` to the target **Point Light Volume Instance**.
3. Make sure the target Point Light Volume has `Shadows` enabled and its shadow settings configured.
4. Set `Resolution`.

> [!NOTE]
> For the best result, keep `Resolution` equal to `Shadow Resolution` in **Light Volume Setup**. However, you can lower the resolution here if you want lower-resolution shadows for this light.

5. Enable `Bake On Enable` if the target should rebake once every time this baker component becomes active.
6. Enable `Realtime` only when the shadow needs to keep updating every frame. This makes the shadow fully realtime and very expensive.
7. Adjust `Realtime Faces Per Frame`. This only affects cubemap shadows. `1` spreads a full cubemap update across 6 frames, while `6` updates all faces in one bake tick. Single-slice Spot Light shadows always update one slice.

> [!NOTE]
> For `Realtime`, `1` spreads cubemap work across frames and `6` updates all faces in one tick. Higher values cost more per frame. `Bake On Enable` triggers one full bake.

8. Keep `Shadow Blur Sample Preset` as low as possible for the result you need. Lowering the blur quality improves GPU performance. However, with realtime shadows, sometimes the bottleneck is on CPU side, so it might cause no actual effect. It can still be very noticeable on **Quest** and **Mobile**.
9. Enable `Spherical Blur` only when `Planar Blur` shows visible cubemap seams or Spot Light projection-edge artifacts. It reduces those artifacts, but costs more GPU work.

> [!IMPORTANT]
> Realtime shadows will only be visible in **Play Mode** and in **VRChat**. Scene view realtime shadows are not supported yet.

The baker uses the target Point Light Volume shadow settings, including `Layer Mask`, `Near Plane`, `Far Clip Plane`, `Bias`, `Blur`, `Contact Hardening` and the light culling range. Configure those on the target light before relying on runtime baking. `Far Clip Plane = 0` is also the normal default for runtime baking; it recalculates the far clip from the light's current culling range before rendering.

**Blur** value `0` completely turns off blur and improves GPU performance, but makes the shadow sharper.
**Contact Hardening** value `0` completely turns off the contact hardening effect and improves GPU performance. It's recommended to keep it `0` in most scenarios, because it can cause artifacts.
When `Spherical Blur` is enabled, runtime `Blur` and `Contact Hardening` both sample in spherical shadow space instead of planar texture space.

> [!TIP]
> For startup-only runtime baking, prefer `Bake In Game` on the Point Light Volume. Use `Point Light Shadow Runtime Baker` with `Bake On Enable` only when you specifically need rebaking when another object or component becomes enabled.

### Runtime Blur Modes

Runtime shadows have two blur modes:

- `Planar Blur` is the cheaper default runtime path. It uses two texture-space blur passes and `Shadow Blur Sample Preset` maps to 30/62/126 total blur taps for Low/Medium/High.
- `Spherical Blur` is the more geometrically correct runtime path. It samples a one-pass radial kernel in spherical shadow space, so cubemap face seams and single-slice Spot Light edge divergence are much less visible. Its blur presets use 33/65/129 taps for Low/Medium/High.

Under the hood, `Planar Blur` treats each shadow slice or cubemap face as a flat texture. It blurs in texture space, first in one axis and then in the other axis. This is fast, but the blur kernel does not follow the real cubemap or Spot Light projection shape. Near cubemap face edges and wide Spot Light projection edges, the apparent blur radius can diverge and make seams more visible.

`Spherical Blur` offsets samples in shadow direction space instead. For cubemap shadows it can cross face edges consistently, and for single-slice Spot Light shadows it keeps the blur closer to a stable angular radius. This reduces visible seams and projection-edge artifacts, but each tap needs extra shadow-space reprojection, so it is more expensive than `Planar Blur`.

Editor `Bake Shadows` and `Bake In Game` use spherical shadow-space blur. The runtime baker option exists so you can choose the cost/artifact tradeoff for realtime baking.

### Single-Slice Realtime Spot Shadows

Single-slice Spot Light shadows are the cheapest realtime option. They are about 6 times cheaper than **Area Lights** and **Point Lights** realtime shadows.
It's the best choice for flashlights, projectors, small stage lights, or other lights that only need to look in one direction.

To use them:

1. Set the light `Type` to `Spot Light`.
2. Keep `Angle` below 180 degrees. A value around 120 degrees or lower is recommended for better quality.
3. Keep `Force Cubemap Shadows` disabled.
4. If this light previously used a cubemap shadow source, delete the baked texture reference in the component.
5. Use the Realtime Shadow Baker with `Realtime` enabled only for heroic lights, single flashlights, or other isolated lights that really need moving objects to cast shadows.

## Runtime Script Control

The native runtime bake method is `PointLightVolumeInstance.BakeShadows()`. It uses the current fields on the target light:

- `RuntimeShadowResolution`
- `RuntimeShadowBlurSamplePreset`
- `RuntimeShadowSphericalBlur`
- `RuntimeShadowFacesPerFrame`
- `RuntimeShadowDirectOutput`

Set those fields first, then call `BakeShadows()`. If `RuntimeShadowFacesPerFrame` is below the required slice count, repeated calls continue the current bake cycle. If resolution, direct output mode or slice count changes while a cycle is in progress, the light starts a new cycle.

`Bake In Game` calls `BakeShadows()` once from `Start()` with high runtime quality, spherical blur and full one-frame baking.

`PointLightShadowRuntimeBaker.BakeShadows()` is now a convenience trigger. It writes its settings into the target Point Light Volume and calls the target's `BakeShadows()`. If `Realtime` is enabled, the baker's delayed loop keeps triggering the target every frame.

If you replace a Point Light Volume shadow source manually from Udon, rebuild the shadow texture cache through the manager after changing the source. Use `NotifyPointLightVolumeChanged()` when changing one light, or `ReinitializeShadowTextures()` after several changes.

If you change `AutoUpdateShadowMap` from Udon, the manager's `AutoUpdateTextures` must also be enabled for the source to update automatically.

Do not call shadow cache rebuild methods every frame. For per-frame shadows, use `PointLightShadowRuntimeBaker` with `Realtime` enabled, or use `Auto Update Textures` only for RenderTexture or Material sources that really need continuous updates.

## Performance Notes

No shadows is always the cheapest mode.

Baked shadows are usually reasonable for static lights. They cost extra VRAM and extra shader sampling, but they do not render shadow maps in runtime. They are still more expensive than the same light without shadows.

`Bake In Game` saves build and asset bundle memory because the editor-baked shadow texture is stripped from runtime state and regenerated once from `Start()`. It still has a startup CPU/GPU cost, so avoid enabling it on many expensive cubemap lights at the same time.

Realtime Shadow Baker is very expensive. It renders shadow camera views in runtime, encodes the depth into EVSM moments, optionally blurs it, and updates the shared shadow array every update. It is more expensive than Unity realtime shadows, so reserve it for heroic lights, single flashlights, or other isolated lights that visibly need moving casters.

For realtime shadows:

- Prefer single-slice Spot Light shadows when possible. Keep the angle below 180 degrees, and around 120 degrees or lower for better quality.
- Use the lowest `Resolution` that still looks acceptable.
- Keep `Shadow Blur Sample Preset` low. It only matters when `Blur` is above `0`.
- Keep `Spherical Blur` disabled unless `Planar Blur` produces visible cubemap seams or Spot Light projection-edge artifacts.
- Keep `Contact Hardening` at `0` unless you really need it.
- Use `Layer Mask` and `Object Mask` to render only actual shadow casters.
- Keep `Realtime Faces Per Frame` low for cubemap shadows unless you need all faces updated immediately.
- Avoid full realtime Point Light and Area Light shadows unless the light is genuinely important.

## Shadow Parameters

| Parameter | Description |
| --- | --- |
|`Shadows` | Enables shadow map sampling for this Point Light Volume. Requires a baked, assigned, auto-updated, or runtime-baked shadow source.|
|`Shadow Map` | Shadow texture source used by this light. Can be generated by `Bake Shadows`, assigned manually, or generated by runtime baking. Supports Cubemap, Texture2DArray, RenderTexture and Material.|
|`Layer Mask` | Layers that can cast shadows during editor or runtime shadow baking.|
|`Object Mask` | Optional object list. If empty, all objects on the selected layers can cast shadows. If not empty, only children of the listed objects are rendered during the bake.|
|`Near Plane` | Near clip plane used by the shadow bake camera. Shadow depth is normalized between `Near Plane` and `Far Clip`, so higher values can improve precision but can clip nearby occluders.|
|`Far Clip Plane` | Far clip plane used by the shadow bake camera. `0` recalculates it from the light's current culling range and is usually the recommended default. Set it manually only to clip distant shadow casters, reduce wasted depth range, or stabilize precision for a known bounded shadow area. Too small values clip valid shadow casters.|
|`Bias` | World-space bias in meters used while baking shadows. Larger values reduce self-shadow artifacts but can detach contact edges.|
|`Blur` | Shadow blur radius applied after baking, normalized to 128x128 shadow resolution. Editor baking and `Bake In Game` use spherical shadow-space blur. Runtime baker realtime mode uses `Planar Blur` unless `Spherical Blur` is enabled. `0` keeps shadows unblurred.|
|`Contact Hardening` | Hardens shadows near contact areas. It can produce artifacts and is more expensive in runtime shadow mode. When `Spherical Blur` is enabled, contact hardening samples use the same spherical shadow-space kernel.|
|`Use World Space` | Keeps baked shadows attached to the baked world-space pose instead of moving them with the light. Less optimized when enabled.|
|`Force Cubemap Shadows` | Forces Spot Light shadows to bake and store as a cubemap even when the spot angle could use a single projected shadow texture.|
|`Rebake Shadows` | Includes this light when pressing `Bake Shadows` in **Light Volume Setup**.|
|`Shadow Resolution` | Resolution used by the shared shadow texture array. For cubemap shadows this value is per face.|
|`Shadow Format` | Read-only precision selected automatically by the active build target. Android/Quest/iOS uses `Half`; PC uses `Float`.|
|`Shadow Bleed Reduction` | Global EVSM visibility remap. Higher values reduce classic EVSM light bleeding and can suppress remaining bright edge artifacts after variance tuning, but can collapse soft penumbra.|
|`Shadow Min Variance` | Global minimum EVSM variance clamp. In **Light Volume Setup** this is a `0..1` logarithmic slider over the raw `0.0001..1.0` range. The PC and Android/Quest/iOS values are stored separately, and the inspector shows the value for the active Unity build target. Higher values reduce Half precision edge, corner and contact-start artifacts but can detach contact shadows.|
|`Bake In Game` | Point Light Volume option. Bakes this light's shadow once from `Start()` in Play Mode or VRChat. The editor can still use an assigned baked shadow texture for preview, but that texture is removed from build/upload runtime state so the bundle does not include it for this light. Uses high runtime bake quality.|
|`Runtime Shadow Resolution` | Point Light Volume runtime bake resolution. `Bake In Game` normally receives this from **Light Volume Setup** during build/upload preparation.|
|`Runtime Shadow Faces Per Frame` | Point Light Volume runtime bake option. Number of cubemap faces processed per `BakeShadows()` trigger. `Bake In Game` uses full one-frame baking.|
|`Runtime Shadow Blur Sample Preset` | Point Light Volume runtime blur and contact hardening sample preset. Lower presets are cheaper.|
|`Runtime Shadow Spherical Blur` | Point Light Volume runtime option. Samples runtime blur and contact hardening in spherical shadow space. `Bake In Game` enables it for better quality.|
|`Bake On Enable` | Realtime Shadow Baker option. Configures the target Point Light Volume and triggers one bake when the baker becomes active. Use this for enable-time rebakes, not for simple startup baking.|
|`Realtime` | Realtime Shadow Baker option. Continuously updates shadow slices through a delayed Udon event loop.|
|`Realtime Faces Per Frame` | Realtime Shadow Baker option. Number of cubemap faces updated per bake tick. Single-slice Spot Light shadows ignore this and update one slice.|
|`Shadow Blur Sample Preset` | Realtime Shadow Baker option. Controls runtime blur and contact hardening sample count. Lower presets are cheaper. `Planar Blur` uses 30/62/126 two-pass blur taps; `Spherical Blur` uses 33/65/129 one-pass blur taps.|
|`Spherical Blur` | Realtime Shadow Baker option. Samples runtime blur and contact hardening in spherical shadow space to reduce visible cubemap and single-slice Spot Light projection seams. More correct, but more expensive than `Planar Blur`.|
