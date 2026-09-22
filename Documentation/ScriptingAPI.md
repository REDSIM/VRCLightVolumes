[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | **Scripting API** | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# UdonSharp API

<a id="scripting-api"></a><a id="in-game"></a>

| Menu |
| --- |
| **UdonSharp API**<br />• [Runtime Rules](#runtime-rules)<br />• [LightVolumeInstance](#lightvolumeinstance)<br />• [PointLightVolumeInstance](#pointlightvolumeinstance)<br />• [LightVolumeManager](#lightvolumemanager)<br />• [Optional Integrations](#optional-integrations) |
| <a id="unity-editor-api"></a><a id="setup"></a><a id="manager-context"></a><a id="authoring-operations"></a><a id="atlas-post-processors"></a><a id="operations"></a><a id="custom-callbacks"></a><a id="custom-lightmapper-operations"></a><a id="in-the-editor"></a>[Unity Editor API](./UnityEditorAPI.md) |
| [Custom Lightmapper Integration](./CustomLightmapperIntegration.md) |

Import `VRCLightVolumes` to control lights from your own UdonSharp methods. The snippets use these references, assigned in the Inspector:

| Example reference | Type | Purpose |
| --- | --- | --- |
| `RoomLight` | `LightVolumeInstance` | A baked Regular or Additive volume. |
| `Lamp` | `PointLightVolumeInstance` | A Point, Spot or Area light. |
| `Manager` | `LightVolumeManager` | The world's Manager. |
| `ShadowBaker` | `PointLightShadowRuntimeBaker` | The optional runtime shadow baker. |

Add `using VRCLightVolumes;` alongside `using UnityEngine;`. Texture and Material variables below represent your assigned source assets. Each snippet shows one operation; they are not a script to run in sequence.

## Runtime rules

- Call setters directly so the lighting data updates with the field. `SendCustomEvent` can't pass their arguments.
- Use `SetColorAndIntensity()` when both values change together. There is no need to call `Manager.UpdateVolumes()` afterward.
- Supply gamma/sRGB `Color` values, as the Inspector color picker does. The Manager converts them to linear lighting; don't convert them with `.linear` first.
- Keep one Manager for the world. Assign it before you enable a spawned light or volume, and keep that reference afterward.
- For continuously moving lights, enable **Dynamic** and Manager **Auto Update Volumes**. For an occasional move, change the Transform and call its update method once.
- Use intensity zero for a frequently switched light. Disabling the component or GameObject removes it from the Manager; enabling it adds it again.
- If scripts add a light type, projection, shadow or rotated volume later, keep that feature enabled in **Shader Stripping** with **Auto** off. See [Shader feature stripping](./ForDevelopers.md#shader-feature-stripping).

Light Volumes changes are local: the core components use `BehaviourSyncMode.None`. For shared controls, sync the state in your own manually synced Udon behaviour. Apply the light setters on each client, including from deserialization for late joiners.

If you use an asmdef, reference `red.sim.LightVolumesUdon` and add a matching UdonSharp assembly definition for your scripts. Otherwise, no new assembly is needed.

## LightVolumeInstance

These methods change an existing baked volume and return `void`.

### Common operations

**`SetColor(Color color)`**

Tint the baked lighting. White keeps its original color.

```csharp
RoomLight.SetColor(new Color(1f, 0.6f, 0.3f));
```

<a id="a-local-light-switch"></a>

**`SetIntensity(float intensity)`**

Scale baked brightness: `1` keeps the original bake and `0` removes its contribution.

```csharp
RoomLight.SetIntensity(0.5f); // Half the baked brightness.
```

For a switchable lamp contribution, use an **Additive** volume so turning it off keeps the room's base lighting.

**`SetColorAndIntensity(Color color, float intensity)`**

Change tint and brightness with one update.

```csharp
RoomLight.SetColorAndIntensity(Color.white, 1f); // Restore the original bake.
```

**`SetDynamic(bool isDynamic)`**

Let the Manager follow this volume's Transform. Enable **Auto Update Volumes** on the Manager first.

```csharp
RoomLight.SetDynamic(true);
```

Moving a baked volume moves its stored lighting; it does not bake new light bounces.

**`SetAdditive(bool isAdditive)`**

Add this volume's baked lighting on top of the base lighting. Bake the added lights separately to avoid counting them twice.

```csharp
RoomLight.SetAdditive(true);
```

**`SetWeight(float weight)`**

Set runtime priority. Higher weights are considered first where volumes overlap.

```csharp
RoomLight.SetWeight(1f); // Prioritize this room over a volume with weight 0.
```

**`SetSmoothBlending(float radius)`**

Set the edge blend distance in world units.

```csharp
RoomLight.SetSmoothBlending(0.25f); // Blend over 25 cm.
```

**`UpdateTransform()`**

Read position, rotation and scale after a one-time move or resize. Continuous motion can use **Dynamic** instead.

```csharp
RoomLight.transform.position = newPosition;
RoomLight.UpdateTransform();
```

Read `Color`, `Intensity`, `IsDynamic`, `IsAdditive`, `SmoothBlending` and `RegistryWeight` for the current state; use the setters to change it.

`Texture0/1/2`, resolution and bake settings belong to Editor setup. Atlas coordinates, rotation rows and inverse matrices are calculated data.

## PointLightVolumeInstance

These methods control Point, Spot and Area lights and return `void`.

### Color, movement and shape

**`SetColor(Color color)`**

Change tint and update the calculated light range.

```csharp
Lamp.SetColor(new Color(1f, 0.6f, 0.3f));
```

**`SetIntensity(float intensity)`**

Change brightness. Use zero for a frequently switched light.

```csharp
Lamp.SetIntensity(0f);
```

**`SetColorAndIntensity(Color color, float intensity)`**

Change color and brightness with one update.

```csharp
Lamp.SetColorAndIntensity(Color.white, 100f);
```

Point Light Volume intensity uses a different scale from Unity Lights. Adjust it for the emitter size and the result you want.

**`SetDynamic(bool isDynamic)`**

Follow Transform changes automatically. Enable **Auto Update Volumes** on the Manager first.

```csharp
Lamp.SetDynamic(true);
```

**`SetWeight(float weight)`**

Set runtime priority; higher weights are considered first.

```csharp
Lamp.SetWeight(1f);
```

**`SetShadingStrength(float strength)`**

Set normal-based shaping and shadow strength, clamped to `0..1`. Zero disables both for this light.

```csharp
Lamp.SetShadingStrength(0.5f);
```

**`SetLightSourceSize(float size)`**

Set the Point/Spot emitter size. In LUT mode, this sets the range instead.

```csharp
Lamp.SetLightSourceSize(0.05f); // A small Point/Spot emitter.
```

**`SetPointLight()`**

Select Point type to emit in all directions.

```csharp
Lamp.SetPointLight();
```

**`SetSpotLight(float angleDeg, float falloff)`**

Select Spot type. The angle is the **full cone angle in degrees**; use `0.001..1` for falloff. Zero falloff can produce an invalid cone calculation.

```csharp
Lamp.SetSpotLight(60f, 0.5f); // A 60-degree cone with a soft edge.
```

**`SetSpotLight(float angleDeg)`**

Change the cone angle while keeping the existing falloff coefficient. Use the two-argument overload to set both.

```csharp
Lamp.SetSpotLight(45f);
```

**`SetAreaLight()`**

Select rectangular Area type. Width and Height come from the absolute world X/Y scale.

```csharp
Lamp.transform.localScale = new Vector3(2f, 1f, 1f);
Lamp.SetAreaLight(); // A 2 by 1 m emitter with an unscaled parent.
```

**`SetSpotCookieAspect(float aspect)`**

Set the Spot cookie's width/height ratio; `1` is square.

```csharp
Lamp.SetSpotCookieAspect(16f / 9f);
```

Use `SetSpotLight()` for degrees; the `Angle` field stores a half-angle in radians. Size Area lights through their Transform instead of writing `Width` or `Height`.

**`UpdateTransform()`**

Read all Transform channels after a move, rotation or resize.

```csharp
Lamp.transform.position = newPosition;
Lamp.transform.rotation = newRotation;
Lamp.UpdateTransform();
```

**`UpdatePosition()`**

Update a position-only change.

```csharp
Lamp.transform.position = newPosition;
Lamp.UpdatePosition();
```

**`UpdateRotation()`**

Update direction, projection rotation and Area cookie mirroring after a rotation-only change.

```csharp
Lamp.transform.rotation = newRotation;
Lamp.UpdateRotation();
```

**`UpdateScale()`**

Update source scale, Area dimensions and range after a scale-only change.

```csharp
Lamp.transform.localScale = new Vector3(2f, 1f, 1f);
Lamp.UpdateScale();
```

### Cookies, materials and LUTs

**`SetCustomTexture(Texture texture)`**

Assign a cookie and select custom projection. RenderTexture-derived sources update live by default; ordinary texture assets are copied when the shared array is rebuilt.

```csharp
Lamp.SetCustomTexture(cookieRenderTexture);
```

Keep Manager **Auto Update Textures** enabled for live sources. Pass `null` to clear the source and return to parametric mode:

```csharp
Lamp.SetCustomTexture(null);
```

**`SetCustomTexture(Texture texture, bool isCubemap, bool autoUpdate)`**

Choose live updates or a snapshot explicitly. The legacy `isCubemap` argument is ignored; the texture determines its layout.

```csharp
Lamp.SetCustomTexture(cookieRenderTexture, false, false); // Keep a snapshot.
```

A snapshot changes only when the shared array is rebuilt.

**`SetCustomMaterial(Material material)`**

Generate the cookie with a Material and enable live updates.

```csharp
Lamp.SetCustomMaterial(cookieMaterial);
```

**`SetCustomMaterial(Material material, bool autoUpdate)`**

Choose live updates (`true`) or a snapshot (`false`). Pass `null` to clear the source.

```csharp
Lamp.SetCustomMaterial(cookieMaterial, false);
```

**`SetLut()`**

Use an assigned 2D texture as a falloff LUT. Assign the texture first; `SetLut()` does not assign one. This example uses a Point light at unit scale.

```csharp
Lamp.SetCustomTexture(falloffLut);
Lamp.SetLut();
Lamp.SetLightSourceSize(5f); // Five-meter range at unit scale.
```

**`SetParametric()`**

Return to analytic falloff while keeping the source reference for later reuse.

```csharp
Lamp.SetParametric();
```

**`SetCustomTexture()`**

Re-select custom projection from the stored source. This parameterless overload supports older integrations; prefer the typed overloads when assigning a new source.

```csharp
Lamp.SetCustomTexture(); // Reuse the source kept by SetParametric().
```

A texture replaces the material source, and a material replaces the texture source. Custom Point projections use six faces; Spot, Area and LUT projections use one slice. See [Point Light Volumes](./HowToUse_PointLightVolumes.md) for source formats and setup.

### Runtime shadow baking

Use an active light with **Shadows** enabled and a Manager assigned. See [shadow setup](./HowToUse_Shadows.md#bake-shadows-via-script), including Shader Stripping settings if scripts enable shadows absent from the authored scene.

**`void BakeShadows()`** captures **one complete shadow** per call: one slice for a projected Spot, or six faces for a Point, Area or cubemap Spot. It returns no success value. Call it after an occasional light or geometry change; profile before using it every frame.

```csharp
Lamp.BakeShadows(); // Refresh shadows after a blocker moves.
```

| Setting | Runtime use |
| --- | --- |
| `NearClip`, `FarClip` | Shadow capture distances. `FarClip = 0` uses the calculated light range. |
| `LayerMask` | Layers visible to the shadow camera. |
| `ExclusionMask` (`Renderer[]`) | Exact Renderer components to hide during capture. Children are not found automatically. Their previous `forceRenderingOff` states are restored afterward. |
| `Bias`, `Blur`, `ContactHardening` | Shadow bias and filtering controls. |
| `ShadowBakeResolution` | Authoring selection: `0` inherits the Manager; explicit choices are `16`, `32`, `64`, `128`, `256`, `512`, `1024`, `2048`. Build preparation resolves this into `RuntimeShadowResolution`. |
| `RuntimeShadowResolution` | Resolution read by `BakeShadows()` at runtime. |
| `RuntimeShadowBlurSamplePreset` | `0` = Low, `1` = Medium, `2` = High. |
| `RuntimeShadowSphericalBlur` | Blur across cubemap face boundaries. |

Lower a light's bake resolution to reduce capture work. The Manager's shadow resolution still controls the shared atlas resolution.

**Bake In Game** queues once in `Start` if the Manager is assigned. An initially inactive light queues on its first activation. The Manager handles at most one complete light per frame. Later activation or color changes don't queue another bake. After a failed request or a late Manager assignment, call `BakeShadows()` yourself.

### PointLightShadowRuntimeBaker

The optional component in `Extra/Shadow Runtime Baker` schedules a target light's `BakeShadows()` method.

| Member | Effect |
| --- | --- |
| `PointLightVolumeInstance TargetPointLightVolume` | Light to bake. |
| `bool BakeOnEnable` | Bake once when enabled, if `Realtime` is off. |
| `bool Realtime` | Repeatedly bake complete shadows for an active target. Configure before enabling the component. |
| `void BakeShadows()` | Request one complete bake immediately. |

For a baker already assigned to its target, request one bake with:

```csharp
ShadowBaker.BakeShadows();
```

Set resolution and blur on the target light. **Realtime** takes priority over **Bake On Enable**; disable the baker to stop repeated bakes. A cubemap shadow renders the scene six times per update, so test on the target device.

## LightVolumeManager

Use the light's setters for ordinary changes. The Manager methods below control shared settings and custom integrations.

### Runtime controls

**`void SetForceSceneLighting(bool enabled)`**

Publish the scene-lighting override used by compatible avatar shaders. Assigning the field alone does not publish it.

```csharp
Manager.SetForceSceneLighting(true);
```

**`void SetClustering(bool enabled)`**

Switch Froxel Clustering. Calling with `true` again permits a retry after a support or allocation failure. Shaders using the VRC Light Volumes 3.x lighting functions support clustering without a separate integration.

```csharp
Manager.SetClustering(true);
```

**`void RequestUpdateVolumes()`**

Request a full update after direct field changes. Repeated requests share one update; light setters already request what they need.

```csharp
Manager.AutoUpdateTextures = true;
Manager.RequestUpdateVolumes();
```

The explicit request restarts texture updates even from ordinary C# after the update process stopped. Udon writes already trigger the field callback.

**`void UpdateVolumes()`**

Rebuild and publish lighting data immediately when a custom integration needs it in the same call. Do not add this after every setter.

```csharp
Manager.UpdateVolumes(); // Publish the completed batch of integration changes.
```

### Settings and outputs

| Members | Guidance |
| --- | --- |
| `AutoUpdateVolumes` | Poll Transform changes on dynamic volumes. Color and intensity setters work without it. |
| `AutoUpdateTextures` | Refresh sources whose per-light automatic-update flags are on. If ordinary C# enables this after the update process stopped, also call `RequestUpdateVolumes()` once. Udon writes trigger its field callback. |
| `LightsBrightnessCutoff` | Threshold for calculated Point Light Volume range. Higher values shorten the light's tail and reduce overlap. |
| `AdditiveMaxOverdraw` | Contribution cap per pixel, applied separately to additive Regular Volumes and Point Light Volumes. |
| `Clustering`, `FroxelDensity`, `FroxelSlices`, `FroxelCoarse`, `ClusteringMinLights` | Clustering settings. Prefer the Inspector; use `SetClustering()` for the runtime switch. |
| `ShadowCulling` | Skip lights for fully shadowed clusters. Off by default; profile before enabling. |
| `LightProbesBlending`, `SharpBounds` | Regular-volume fallback and outer-edge blending controls. |
| `CustomTexturesWidth`, `CustomTexturesHeight` | Shared projection resolution. |
| `ShadowTexturesWidth`, `ShadowTexturesHeight`, `ShadowTextureFormat` | Shared shadow resolution and precision (`0` = Half, `1` = Float). Prefer target-specific Inspector settings. |
| `ShadowBleedReduction`, `ShadowMinVariance` | Global shadow artifact controls. Configure through the Inspector. |
| `LightVolumeAtlas` | Final runtime voxel atlas, including Editor post-processing. |
| `CustomTextures`, `CubemapsCount` | Shared projection array and its cubemap count; read-only outputs for integrations. |
| `ShadowTextures`, `ShadowCubemapsCount`, `ShadowMapsCount` | Shared shadow array and layout counts; read-only outputs. |
| `LightVolumeInstances`, `PointLightVolumeInstances` | Manager-owned registries. Do not edit the arrays. |
| `EnabledCount`, `EnabledIDs` | Advanced view of enabled Regular Volume IDs. Read only `[0, EnabledCount)` and do not modify or retain the array. |

Use the Inspector for setup and the documented setters for runtime changes. Public fields also include baked and calculated data; direct writes don't always update the lighting. `ShaderStripping`, `AutoShaderFeatures` and `ShaderFeatures` apply in the Editor and during builds.

### Advanced texture integration

Use these calls when writing your own source updater or baker. Normal light setters and the built-in baker already handle their updates.

**`void ReinitializeCustomTextures()`**

Rebuild projection caches after a direct source change. Normal source setters do this for you.

```csharp
Manager.ReinitializeCustomTextures();
Manager.UpdateVolumes(); // Publish the rebuilt source IDs.
```

**`void UpdateAutoCustomTextures()`**

Copy sources marked for automatic updates. The Manager normally calls this; use it for a deliberate manual refresh.

```csharp
Manager.UpdateAutoCustomTextures();
```

**`int GetPointLightCustomID(PointLightVolumeInstance instance)`**

Read the current projection ID, or `-1` if none. Do not keep it across source or layout changes.

```csharp
int projectionId = Manager.GetPointLightCustomID(Lamp);
if (projectionId < 0) return;
```

**`void ReinitializeShadowTextures()`**

Rebuild from configured shadow sources and metadata. This does not assign a new shadow source.

```csharp
Manager.ReinitializeShadowTextures(); // Retry after fixing a failed atlas allocation.
```

**`void UpdateAutoShadowTextures()`**

Refresh shadow sources marked for automatic updates. This copies source data; it does not capture scene geometry.

```csharp
Manager.UpdateAutoShadowTextures();
```

**`void RecalculatePointLightRange(PointLightVolumeInstance instance)`**

Resolve the calculated range before a custom baker encodes depth. Normal setters and runtime baking handle it as needed.

```csharp
Manager.RecalculatePointLightRange(Lamp);
float range = Mathf.Sqrt(Lamp.SquaredRange);
```

**`bool UpdatePointLightShadowTexture(PointLightVolumeInstance instance)`**

Publish a complete retained shadow source. `true` means it is present in the current atlas; `false` means invalid source/layout or allocation failure.

```csharp
bool copied = Manager.UpdatePointLightShadowTexture(Lamp);
```

Call after your integration fills the light's configured source texture, with all required faces complete.

**`int PreparePointLightDirectShadowOutput(PointLightVolumeInstance instance)`**

Get the final atlas base slice, or `-1` if unavailable. The built-in realtime baker uses this for direct output.

```csharp
int baseSlice = Manager.PreparePointLightDirectShadowOutput(Lamp);
if (baseSlice < 0) return;
```

This requires an active light already configured for direct output, with capture resolution matching both Manager shadow dimensions. The caller must write every required slice. Prefer the built-in baker for ordinary runtime shadows.

The next two calls publish prepared runtime data. They do not recalculate a changed Transform or update active state after a direct color or intensity write. Use the instance setters for those changes.

**`void NotifyLightVolumeChanged(LightVolumeInstance instance, bool rebuildFinalData)`**

Notify the Manager after an advanced integration updates volume data directly. Set `rebuildFinalData` when the active list must be rebuilt.

```csharp
Manager.NotifyLightVolumeChanged(RoomLight, false); // Publish updated volume data.
```

**`void NotifyPointLightVolumeChanged(PointLightVolumeInstance instance, bool rebuildFinalData, bool customTexturesChanged, bool shadowTexturesChanged)`**

Notify after direct light or source changes. Mark the source cache that changed; use `rebuildFinalData` for an active-list change.

```csharp
Manager.NotifyPointLightVolumeChanged(Lamp, false, true, false);
```

This example marks a directly changed projection source. Use `SetCustomTexture()` or `SetCustomMaterial()` for ordinary source assignments.

A normal runtime bake keeps a complete source texture on the light. With an unchanged layout, later bakes copy that source into the atlas. After allocation fails, `UpdatePointLightShadowTexture()` returns `false` until `ReinitializeShadowTextures()` or a layout change allows another attempt.

The realtime baker can write directly into the atlas when its active target's resolution matches the Manager. The Manager preserves matching direct outputs during atlas rebuilds. Let the baker manage `RuntimeShadowDirectOutput`; use a one-shot bake when you need a retained source after realtime stops. See [shadow output details](./TechnicalDetails.md#realtime-shadow-output).

Leave registration callbacks, `_onVarChange_*`, `_RealtimeBakeLoop`, GPU callbacks and cleanup helpers to the components. They are public for Udon events and component communication. Enabling and disabling a component already adds and removes it from the Manager.

## Optional integrations

- [AudioLink Integration](./HowToUse_AudioLinkIntegration.md): `LightVolumeAudioLink`.
- [TV Screens Integration](./HowToUse_TVScreensIntegration.md#older-workflow-lightvolumetvgi): `LightVolumeTVGI`.
