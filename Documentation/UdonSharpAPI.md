[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | **UdonSharp API** | [Unity Editor API](./UnityEditorAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# UdonSharp API

Use these APIs to switch lights, animate their color, move volumes, change projections or request a shadow bake. Import `VRCLightVolumes` and reference the same components that you configure in the Inspector:

| Component | Purpose |
| --- | --- |
| `LightVolumeInstance` | A baked Regular Light Volume, including additive volumes. |
| `PointLightVolumeInstance` | A Point, Spot or Area Light Volume. |
| `LightVolumeManager` | The world's shared lighting data and settings. |
| `PointLightShadowRuntimeBaker` | Optional component for repeated or on-enable shadow bakes. |

Editor baking and atlas tools have a separate [Unity Editor API](./UnityEditorAPI.md).

Start with the [local switch](#a-local-light-switch), then use the reference for [baked volumes](#lightvolumeinstance), [Point, Spot and Area lights](#pointlightvolumeinstance), or [Manager controls](#lightvolumemanager). [Advanced texture integration](#advanced-texture-integration) is only needed when the normal setters cannot perform your update.

## A local light switch

Create an UdonSharp script named `LocalLampSwitch.cs`. Attach it to an object with a Collider, then drag a Point Light Volume into **Lamp**. Interacting switches that light between zero and **On Intensity** on the local player's client.

```csharp
using UdonSharp;
using UnityEngine;
using VRCLightVolumes;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class LocalLampSwitch : UdonSharpBehaviour {
    [Tooltip("The Point, Spot or Area Light Volume to switch.")]
    public PointLightVolumeInstance Lamp;
    [Tooltip("Light intensity while the switch is on.")]
    public float OnIntensity = 100f;

    private bool _isOn = true;

    // Start with the configured on intensity.
    private void Start() {
        Lamp.SetIntensity(OnIntensity);
    }

    // Change only this client's light.
    public override void Interact() {
        _isOn = !_isOn;
        Lamp.SetIntensity(_isOn ? OnIntensity : 0f);
    }
}
```

For a baked volume, change the field type to `LightVolumeInstance` and start with **On Intensity = 1**. The same setter works. Use an **Additive** baked volume if the switch should add and remove a lamp's baked contribution without replacing the room's base lighting.

The package's core components use `BehaviourSyncMode.None`; their changes are local. For a shared switch, synchronize the switch state in your own manually synced Udon behaviour, then apply `SetIntensity()` from that state on each client, including on deserialization for late joiners. Calling a Light Volumes setter does not send a network update.

## Runtime rules

- Call typed setters directly. They update the derived lighting data as well as the visible field. `SendCustomEvent` cannot pass setter arguments.
- Use `SetColorAndIntensity()` when both values change together. There is no need to call `Manager.UpdateVolumes()` afterward.
- Supply `Color` values in the same gamma/sRGB convention as the Inspector color picker. The Manager converts them to linear lighting; do not pass a color that you have already converted with `.linear`.
- Keep one Manager for the world. Assign it before enabling a spawned light or volume, and do not replace it after registration.
- For continuously moving lights, enable **Dynamic** and Manager **Auto Update Volumes**. For an occasional move, change the Transform and call its update method once.
- Use intensity zero for a frequently switched light. Repeatedly disabling its component or GameObject removes and re-adds it to the Manager's registry.
- When scripts introduce a feature not present at build time, retain it in **Shader Stripping** with **Auto** off. This includes new light types, projections, shadows and rotated volumes. See [Shader feature stripping](./ForDevelopers.md#shader-feature-stripping).

If your scripts use assembly definitions, reference `red.sim.LightVolumesUdon` and configure a matching UdonSharp assembly definition for your own scripts. Ordinary scripts outside a custom asmdef do not need a new assembly just to use this API.

## LightVolumeInstance

### Common operations

```csharp
// Fade or recolor an existing baked lighting contribution.
RoomLight.SetColorAndIntensity(new Color(1f, 0.6f, 0.3f), 0.5f);

// Move it once. The stored lighting moves with the volume; this does not rebake the room.
RoomLight.transform.position = newPosition;
RoomLight.UpdateTransform();
```

| Method | Effect |
| --- | --- |
| `SetColor(Color color)` | Multiply the baked lighting by this tint. |
| `SetIntensity(float intensity)` | Scale its brightness. Zero removes its contribution. |
| `SetColorAndIntensity(Color color, float intensity)` | Set both with one update. |
| `SetDynamic(bool isDynamic)` | Enable or disable Manager-driven Transform updates. Requires Manager `AutoUpdateVolumes` when enabled. |
| `SetAdditive(bool isAdditive)` | Switch between base and additive lighting. |
| `SetWeight(float weight)` | Change runtime priority. Higher weights are considered first. |
| `SetSmoothBlending(float radius)` | Change the edge blend distance in world units. |
| `UpdateTransform()` | Read position, rotation and scale and update the lighting data. |

All methods return `void`. Read `Color`, `Intensity`, `IsDynamic`, `IsAdditive`, `SmoothBlending` and `RegistryWeight` for the current state; use the setters to change it.

`Texture0/1/2`, resolution and bake settings are Editor authoring data. Atlas coordinates, rotation rows and inverse matrices are derived data. Do not edit these fields to move or recolor a volume.

## PointLightVolumeInstance

### Color, movement and shape

| Method | Effect |
| --- | --- |
| `SetColor(Color color)` | Change tint and update the calculated range. |
| `SetIntensity(float intensity)` | Change brightness and update the calculated range. |
| `SetColorAndIntensity(Color color, float intensity)` | Change both with one update. |
| `SetDynamic(bool isDynamic)` | Enable or disable Manager-driven Transform updates. |
| `SetWeight(float weight)` | Change runtime priority. Higher weights are considered first. |
| `SetShadingStrength(float strength)` | Set normal-based shaping and shadow strength, clamped to `0..1`. Zero disables both for this light. |
| `SetLightSourceSize(float size)` | Set Point/Spot physical source size. In LUT mode this controls its range instead. |
| `SetPointLight()` | Select Point type. |
| `SetSpotLight(float angleDeg, float falloff)` | Select Spot type with a **full cone angle in degrees** and edge falloff. Use `0.001..1` for falloff; zero can produce an invalid cone calculation. |
| `SetSpotLight(float angleDeg)` | Select Spot type and change its cone angle, keeping the existing falloff coefficient. Use the two-argument overload when you want to define both. |
| `SetAreaLight()` | Select rectangular Area type. Width and Height come from the absolute world X/Y scale. |
| `SetSpotCookieAspect(float aspect)` | Set a Spot cookie's width/height aspect. `1` is square. |
| `UpdateTransform()` | Read all Transform channels and update changed data. |
| `UpdatePosition()` | Update a known position-only change. |
| `UpdateRotation()` | Update direction, projection rotation and Area cookie mirroring. |
| `UpdateScale()` | Update source scale, Area dimensions and range. |

All methods return `void`. For example:

```csharp
Lamp.transform.position = newPosition;
Lamp.UpdatePosition();

// A 60-degree spotlight with a soft cone edge.
Lamp.SetSpotLight(60f, 0.5f);

// A two-meter-wide rectangular light. Use an unscaled parent for these dimensions.
Lamp.transform.localScale = new Vector3(2f, 1f, 1f);
Lamp.SetAreaLight();
```

The Inspector displays Spot angles in degrees, but the `Angle` field stores a half-angle in radians. Use `SetSpotLight()` instead of assigning `Angle` directly. Likewise, size an Area light through its Transform instead of directly assigning the derived `Width` and `Height` fields.

### Cookies, materials and LUTs

```csharp
// A live camera/video RenderTexture cookie. Manager AutoUpdateTextures must be enabled.
Lamp.SetCustomTexture(cookieRenderTexture);

// Use a 2D falloff LUT already imported into the project.
Lamp.SetCustomTexture(falloffLut);
Lamp.SetLut();
Lamp.SetLightSourceSize(5f);

// Clear the projection and use analytic falloff again.
Lamp.SetCustomTexture(null);
```

| Method | Effect |
| --- | --- |
| `SetCustomTexture(Texture texture)` | Assign a projection and switch to custom mode. RenderTexture and Custom Render Texture sources update live by default; ordinary texture assets are copied when the shared array is rebuilt. `null` clears the source and selects parametric mode. |
| `SetCustomTexture(Texture texture, bool isCubemap, bool autoUpdate)` | Explicitly choose live updates or a snapshot. The legacy `isCubemap` argument is ignored; source layout is inferred from the texture. |
| `SetCustomMaterial(Material material)` | Assign a generated projection, select custom mode and enable live updates. |
| `SetCustomMaterial(Material material, bool autoUpdate)` | Choose live updates (`true`) or a snapshot taken when the shared array is rebuilt (`false`). `null` clears the source. |
| `SetLut()` | Use the already assigned 2D texture as a falloff LUT. It does not assign a texture. |
| `SetParametric()` | Use analytic falloff. Keeps the source reference for later reuse. |
| `SetCustomTexture()` | Re-select custom projection after assigning source fields directly. Kept for older integrations; prefer a typed source overload. |

All methods return `void`. Assigning a texture clears the material source; assigning a material clears the texture source. The light type chooses the destination shape: a custom Point projection uses six faces, while Spot, Area and LUT projections use one slice.

To stop refreshing a live source, call the explicit overload with `autoUpdate = false`. Changes to a snapshot's content appear only after a rebuild; use live mode for animated content. See [Point Light Volumes](./HowToUse_PointLightVolumes.md) for source formats and projection setup.

### Runtime shadow baking

Configure the light through the [shadow setup](./HowToUse_Shadows.md) first. Enable **Shadows** and either **Bake In Game** or assign it to a **Point Light Shadow Runtime Baker** so build preparation supplies the required camera and materials. Merely calling `BakeShadows()` on an unprepared light does not create those dependencies.

`void BakeShadows()` captures and publishes **one complete shadow** per call: one slice for a projected Spot, or six faces for a Point, Area or cubemap Spot. It returns no success value. Use it after an occasional lighting/geometry change; do not put it in your own per-frame loop without profiling.

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

The light's bake resolution controls capture cost and its retained source texture. The Manager's shadow resolution controls the shared atlas. Lowering only a light's bake resolution does not reduce that atlas's memory use.

**Bake In Game** queues once when the light first reaches `Start`, provided its Manager is assigned then. The Manager processes at most one complete light per frame. An initially inactive light queues on its first activation; later enable/disable or color changes do not queue another bake. Assigning the Manager after `Start` does not replay the request. Failed requests are not automatically retried; after fixing the cause, call `BakeShadows()` explicitly.

### PointLightShadowRuntimeBaker

The optional component in `Extra/Shadow Runtime Baker` schedules a target light's `BakeShadows()` method.

| Member | Effect |
| --- | --- |
| `PointLightVolumeInstance TargetPointLightVolume` | Light to bake. |
| `bool BakeOnEnable` | Bake once when enabled, if `Realtime` is off. |
| `bool Realtime` | Repeatedly bake complete shadows for an active target. Configure before enabling the component. |
| `void BakeShadows()` | Request one complete bake immediately. |

Resolution and blur settings come from the target light. Realtime mode takes priority over **Bake On Enable**. Disable the baker to stop its loop. Realtime capture can require six scene renders per update; test on the target device, especially Quest.

## LightVolumeManager

The Manager owns the atlas textures, light registries and shader globals. Most integrations only need a reference to it and the setters on their lights.

### Runtime controls

| Method | Effect |
| --- | --- |
| `void SetForceSceneLighting(bool enabled)` | Publish the scene-lighting override used by compatible avatar shaders. Assigning the field alone does not publish it. |
| `void SetClustering(bool enabled)` | Switch Froxel Clustering. Calling `true` again also permits a retry after a previous support/allocation failure. It cannot restore clustering stripped from the shader. |
| `void RequestUpdateVolumes()` | Request one coalesced full update after advanced direct state changes. Normal light setters already notify the Manager. |
| `void UpdateVolumes()` | Rebuild and upload immediately. Use only when your integration requires an explicit synchronization point, not once per light. |

### Settings and outputs

| Members | Guidance |
| --- | --- |
| `AutoUpdateVolumes` | Poll Transform changes on dynamic volumes. Color and intensity setters work without it. |
| `AutoUpdateTextures` | Refresh sources whose per-light automatic-update flags are on. If ordinary C# enables this after the update process stopped, also call `RequestUpdateVolumes()` once. Udon writes trigger its field callback. |
| `LightsBrightnessCutoff` | Threshold for calculated Point Light Volume range. Higher values shorten the light's tail and reduce overlap. |
| `AdditiveMaxOverdraw` | Contribution cap per pixel, applied separately to additive Regular Volumes and Point Light Volumes. |
| `Clustering`, `FroxelDensity`, `FroxelSlices`, `FroxelCoarse`, `ClusteringMinLights` | Clustering settings. Prefer the Inspector; use `SetClustering()` for the runtime switch. |
| `ShadowCulling` | Optional rejection of fully shadowed clusters. Off by default; profile before enabling. |
| `LightProbesBlending`, `SharpBounds` | Regular-volume fallback and outer-edge blending controls. |
| `CustomTexturesWidth`, `CustomTexturesHeight` | Shared projection resolution. |
| `ShadowTexturesWidth`, `ShadowTexturesHeight`, `ShadowTextureFormat` | Shared shadow resolution and precision (`0` = Half, `1` = Float). Prefer target-specific Inspector settings. |
| `ShadowBleedReduction`, `ShadowMinVariance` | Global shadow artifact controls. Configure through the Inspector. |
| `LightVolumeAtlas` | Final runtime voxel atlas, including Editor post-processing. |
| `CustomTextures`, `CubemapsCount` | Shared projection array and its cubemap count; read-only outputs for integrations. |
| `ShadowTextures`, `ShadowCubemapsCount`, `ShadowMapsCount` | Shared shadow array and layout counts; read-only outputs. |
| `LightVolumeInstances`, `PointLightVolumeInstances` | Manager-owned registries. Do not edit the arrays. |
| `EnabledCount`, `EnabledIDs` | Advanced view of enabled Regular Volume IDs. Read only `[0, EnabledCount)` and do not modify or retain the array. |

Public fields include authoring and derived data, so direct writes are not a general runtime settings API. Use the Inspector for setup, documented setters for changes, and the advanced calls below only when implementing your own integration. `ShaderStripping`, `AutoShaderFeatures` and `ShaderFeatures` are Editor/build settings, not runtime shader switches.

### Advanced texture integration

| Method | Contract |
| --- | --- |
| `void ReinitializeCustomTextures()` | Rebuild projection caches after a direct source change. Normal source setters do this for you. |
| `void UpdateAutoCustomTextures()` | Refresh auto-updated projections. Normally called by the Manager. |
| `int GetPointLightCustomID(PointLightVolumeInstance instance)` | Get the current projection ID, or `-1` if none. Do not keep the ID across source/layout changes. |
| `void ReinitializeShadowTextures()` | Rebuild from already configured shadow sources and metadata. It is not a shadow-source setter. |
| `void UpdateAutoShadowTextures()` | Refresh auto-updated shadows. Normally called by the Manager. |
| `void RecalculatePointLightRange(PointLightVolumeInstance instance)` | Resolve a dirty light range immediately. Normal setters and runtime baking handle this as needed. |
| `bool UpdatePointLightShadowTexture(PointLightVolumeInstance instance)` | Publish a complete persistent shadow source. `true` means it is present in the current atlas. `false` means the source/layout is invalid or allocation failed. |
| `int PreparePointLightDirectShadowOutput(PointLightVolumeInstance instance)` | Resolve the final atlas base slice for source-less direct output, or `-1` if unavailable. Used by the built-in realtime baker. |
| `void NotifyLightVolumeChanged(LightVolumeInstance instance, bool rebuildFinalData)` | Notify the Manager after advanced direct volume changes without a setter. |
| `void NotifyPointLightVolumeChanged(PointLightVolumeInstance instance, bool rebuildFinalData, bool customTexturesChanged, bool shadowTexturesChanged)` | Notify after advanced direct light/source changes without a setter. |

Ordinary runtime baking retains a complete source texture on the light. Repeat bakes with an unchanged layout can copy just that source into the atlas. After an atlas allocation failure, `UpdatePointLightShadowTexture()` returns `false` without repeatedly reallocating; an explicit `ReinitializeShadowTextures()` or real layout change allows another attempt.

The built-in realtime baker can instead write directly into the shared atlas when its active target's resolution matches the Manager. This avoids retaining a second full-size source. Such pixels cannot be reconstructed from a source after an atlas reallocation; the realtime loop writes them again on its next update. Let the built-in baker manage `RuntimeShadowDirectOutput` and its scratch resources.

Do not call registration callbacks, `_onVarChange_*`, `_RealtimeBakeLoop`, GPU callbacks or cleanup helpers yourself. They are public for Udon component/event communication, not a stable integration API. Component enable/disable already handles registration and removal.

## Optional integrations

- [AudioLink Integration](./HowToUse_AudioLinkIntegration.md): `LightVolumeAudioLink`.
- [TV Screens Integration](./HowToUse_TVScreensIntegration.md): `LightVolumeTVGI`.
