[VRC Light Volumes](../README.md) | [How to Use](../Documentation/HowToUse.md) | [Best Practices](../Documentation/BestPractices.md) | **Udon Sharp API** | [For Developers](../Documentation/ForDevelopers.md) | [Compatible Shaders](../Documentation/CompatibleShaders.md)

# Udon Sharp API

| Menu |
| --- |
|**Udon Sharp API**<br />• [Light Volume Manager](#LightVolumeManager)<br />• [Light Volume Instance](#LightVolumeInstance)<br />• [Point Light Volume Instance](#PointLightVolumeInstance)<br />• [Point Light Shadow Runtime Baker](#PointLightShadowRuntimeBaker)|

## LightVolumeManager
Stores the Light Volume atlas, Point Light Volume texture arrays and references to all runtime Light Volume instances. Controls and updates all shader globals used by the Light Volumes system.

### Public Fields
| Public Field | Description |
| --- | --- |
|`Texture3D LightVolumeAtlasBase` | Combined Texture3D containing all baked Light Volume SH data. This field is not used directly at runtime when post processors are used, see `LightVolumeAtlas` instead. |
|`Texture LightVolumeAtlas` | Combined runtime texture containing all Light Volumes' baked SH data. |
|`int CustomTexturesWidth` | Width of each runtime Point Light Volume projection texture slice. |
|`int CustomTexturesHeight` | Height of each runtime Point Light Volume projection texture slice. |
|`float LightsBrightnessCutoff` | Minimum brightness used to cull Point Light Volumes. Larger values improve performance by shrinking light range, but make attenuation less physically correct. |
|`int ShadowTexturesWidth` | Width of each runtime shadow map slice. |
|`int ShadowTexturesHeight` | Height of each runtime shadow map slice. |
|`int ShadowTextureFormat` | Precision used for EVSM shadow maps and the runtime shadow texture array. `0` = `ARGBHalf`, `1` = `ARGBFloat`. The authoring Setup component assigns this automatically from the active build target. |
|`float ShadowBleedReduction` | Global EVSM light bleeding correction. `0` disables it, `1` is strongest. Higher values can collapse soft penumbra. |
|`float ShadowMinVariance` | Raw global minimum EVSM variance used by the receiver shader. The authoring Setup component stores separate PC and Android/Quest/iOS `0..1` logarithmic sliders, but the Udon manager stores only the resolved raw value for the active build target. |
|`bool LightProbesBlending` | When enabled, areas outside Light Volumes fall back to Unity Light Probes. Otherwise, the Light Volume with the smallest weight is used as fallback. It also improves performance. |
|`bool SharpBounds` | Disables smooth blending with areas outside Light Volumes. Use it if your entire scene's play area is covered by Light Volumes. It also improves performance. |
|`bool AutoUpdateVolumes` | Automatically updates transform data for volumes marked `IsDynamic`. Enabling/disabling, `Color` and `Intensity` changes update without this option. |
|`bool AutoUpdateTextures` | Automatically updates dynamic Point Light Volume cookie, LUT, cubemap and shadow sources, such as RenderTextures or Materials. |
|`int AdditiveMaxOverdraw` | Limits the maximum number of additive Light Volumes and Point Light Volumes that can affect a single pixel. |
|`bool ForceSceneLighting` | When enabled, explicitly enables the Force Scene Lighting shader override during manager startup, disabling min/max brightness limits in compatible avatar shaders. When disabled, startup leaves the existing global override unchanged. Use `SetForceSceneLighting` for manual runtime control. |
|`LightVolumeInstance[] LightVolumeInstances` | All registered Light Volume instances. You can enable or disable volume GameObjects at runtime. Manually disabling unnecessary volumes improves performance. |
|`PointLightVolumeInstance[] PointLightVolumeInstances` | All registered Point Light Volume instances. You can enable or disable point light GameObjects at runtime. Manually disabling unnecessary point lights improves performance. |
|`RenderTexture CustomTextures` | Runtime texture array used for Point Light Volume cubemaps, LUTs and cookies. Cubemap faces are stored first, 6 slices per cubemap. Area Light cookies use its mip chain for textured emission and old-shader average-color fallback. |
|`int CubemapsCount` | Number of cubemaps stored in `CustomTextures`. Cubemap faces start from the beginning, 6 elements per cubemap. |
|`bool HasAutoCustomTextureUpdates` | Internal state. True when at least one projection source needs per-frame texture updates. |
|`RenderTexture ShadowTextures` | Runtime texture array used for Point Light Volume shadow maps. |
|`int ShadowCubemapsCount` | Number of cubemap shadow maps stored in `ShadowTextures`. Cubemap shadow maps use 6 slices each and are stored first. |
|`int ShadowMapsCount` | Total shadow map count stored in `ShadowTextures`. Cubemap shadows use 6 array slices, single projected shadows use 1 slice. |
|`bool HasAutoShadowTextureUpdates` | Internal state. True when at least one shadow source needs per-frame texture updates. |
|`Material CubemapFaceMaterial` | Internal material used to copy cubemap faces into runtime texture arrays. You usually don't need to touch this field manually. |
|`Camera RuntimeShadowCamera` | Internal shared disabled camera used by Point Light Volume runtime shadow baking. Prepared by editor/build preprocessing. |
|`Material RuntimeShadowDepthEncodeMaterial` | Internal shared material used to encode runtime shadow camera depth into EVSM moments. Prepared by editor/build preprocessing. |
|`Material RuntimeShadowBlurMaterial` | Internal shared material used by runtime shadow blur passes. Prepared by editor/build preprocessing. |

### Public Properties
| Public Property | Description |
| --- | --- |
|`int EnabledCount` | Number of currently enabled regular Light Volumes after manager culling and sorting. |
|`int[] EnabledIDs` | Registry indices of currently enabled regular Light Volumes. This is mostly useful for advanced custom Udon integrations. |

### Public Methods
| Public Method | Description |
| --- | --- |
|`void NotifyLightVolumeChanged(LightVolumeInstance lightVolume, bool rebuildFinalData)` | Notifies the manager that a Light Volume instance changed. Instance methods call this automatically. Use `rebuildFinalData` when activation, ordering, additive state or atlas data requires a full rebuild. |
|`void NotifyPointLightVolumeChanged(PointLightVolumeInstance pointLightVolume, bool rebuildFinalData, bool customTexturesChanged, bool shadowTexturesChanged)` | Notifies the manager that a Point Light Volume instance changed. Use the boolean flags to rebuild point light data, projection texture caches or shadow texture caches. |
|`void SetForceSceneLighting(bool enabled)` | Explicitly sets the Force Scene Lighting shader override at runtime. Unlike the startup checkbox, this method can enable or disable it on demand. Regular `UpdateVolumes` calls do not reassert the value. |
|`void InitializeLightVolume(LightVolumeInstance lightVolume)` | Registers a Light Volume instance at runtime. Called automatically by `LightVolumeInstance.Start()` / `OnEnable()` when `LightVolumeManager` is assigned, and when the manager reference is assigned later. |
|`void DeinitializeLightVolume(LightVolumeInstance lightVolume)` | Removes a Light Volume instance from the runtime registry without resizing the array. Called automatically on disable. |
|`void InitializePointLightVolume(PointLightVolumeInstance pointLightVolume)` | Registers a Point Light Volume instance at runtime. Called automatically by `PointLightVolumeInstance.Start()` / `OnEnable()` when `LightVolumeManager` is assigned, and when the manager reference is assigned later. |
|`void DeinitializePointLightVolume(PointLightVolumeInstance pointLightVolume, bool customTexturesChanged, bool shadowTexturesChanged)` | Removes a Point Light Volume instance from the runtime registry and optionally invalidates projection or shadow texture caches. Called automatically on disable. |
|`void ReinitializeCustomTextures()` | Rebuilds the shared runtime texture array for Point Light Volume LUTs, cookies and cubemaps. Call this after changing projection sources manually. Sources are deduplicated by source object and auto-update mode, so the same source can have one static slice and one auto-updated slice if both modes are used. |
|`void UpdateAutoCustomTextures()` | Updates only projection sources marked for per-frame refresh. Usually called automatically when `AutoUpdateTextures` is enabled. |
|`void ReinitializeShadowTextures()` | Rebuilds the shared runtime texture array for Point Light Volume shadow maps. Call this after changing shadow sources manually. |
|`void UpdateAutoShadowTextures()` | Updates only shadow sources marked for per-frame refresh. Usually called automatically when `AutoUpdateTextures` is enabled. |
|`void UpdatePointLightShadowTextureSlice(PointLightVolumeInstance instance, int sourceSlice)` | Copies one shadow source slice into the shared shadow texture array. `PointLightVolumeInstance.BakeShadows()` uses this for local runtime output when it needs to publish completed slices. |
|`int GetPointLightCustomID(PointLightVolumeInstance instance)` | Returns the resolved projection texture ID for a Point Light Volume instance, or `-1` if none is assigned. |
|`void RecalculatePointLightRange(PointLightVolumeInstance instance)` | Immediately recalculates one Point Light Volume's canonical culling range. `BakeShadows()` calls this automatically before resolving an automatic `FarClip` when the range is dirty. |
|`void RequestUpdateVolumes()` | Schedules a Light Volume data update on the next delayed update tick. Prefer this over calling `UpdateVolumes()` repeatedly. |
|`void UpdateVolumes()` | Immediately rebuilds and uploads all Light Volume and Point Light Volume shader data. Useful when you intentionally manage updates manually instead of relying on delayed requests. |

## LightVolumeInstance
Stores all runtime regular Light Volume configuration including atlas UVW data, transform data, color and intensity.

When changing a Light Volume from another Udon script, prefer the setter methods below over direct field writes. They skip unchanged values and notify the manager with the smallest required update path.

### Public Fields
| Public Field | Description |
| --- | --- |
|`bool IsDynamic` | Defines whether this volume can be moved in runtime. Disabling this option slightly improves performance. |
|`bool IsAdditive` | Additive volumes apply their light on top of others as an overlay. Useful for movable and togglable lights. They can also project light onto static lightmapped objects if the surface shader supports it. |
|`Color Color` | Multiplies volume color by this value. Changing the color is useful for animating Additive volumes. |
|`float Intensity` | Multiplies the volume color. Basically controls brightness. |
|`Vector4 InvLocalEdgeSmoothing` | Inversed edge smoothing in local atlas space. Recalculates via `SetSmoothBlending(float radius)`. |
|`Vector4 BoundsUvwMin0` | Min bounds of Texture0 in 3D atlas space. `w` stores atlas scale X. |
|`Vector4 BoundsUvwMin1` | Min bounds of Texture1 in 3D atlas space. `w` stores atlas scale Y. |
|`Vector4 BoundsUvwMin2` | Min bounds of Texture2 in 3D atlas space. `w` stores atlas scale Z. |
|`Quaternion InvBakedRotation` | Inverse rotation of the pose the volume was baked in. Needed for dynamic rotated volumes. |
|`Matrix4x4 InvWorldMatrix` | Inversed TRS matrix of this volume that transforms world positions into the 1x1x1 local volume cube. |
|`Vector3 RelativeRotationRow0` | Current volume rotation matrix row 0 relative to the rotation it was baked with. Mandatory for dynamic rotated volumes. |
|`Vector3 RelativeRotationRow1` | Current volume rotation matrix row 1 relative to the rotation it was baked with. Mandatory for dynamic rotated volumes. |
|`bool IsRotated` | True if there is any relative rotation. No relative rotation improves shader performance. |
|`LightVolumeManager LightVolumeManager` | Reference to the Light Volume Manager. Needed for runtime registration and updates. Assigning it after `Start()` / `OnEnable()` also registers the instance automatically. |
|`int RegistryOrder` | Current stable manager registry tie-breaker used when `RegistryWeight` values are equal. It is assigned automatically by the manager; read it for diagnostics or custom integrations, but do not set it manually. |
|`float RegistryWeight` | Current manager registry sort weight. Higher weights are selected first when active volumes exceed the shader limit. Read this field to get the current weight; use `SetWeight(float weight)` to change it so the manager can reorder the registry. |
|`bool IsActive` | Internal active state used by the manager. It becomes false when the GameObject is inactive, intensity is zero, or color is black. |

### Public Methods
| Public Method | Description |
| --- | --- |
|`void SetDynamic(bool isDynamic)` | Sets dynamic mode and rebuilds the manager volume list only when the value changes. |
|`void SetAdditive(bool isAdditive)` | Sets additive mode and rebuilds the manager volume list only when the value changes. |
|`void SetWeight(float weight)` | Sets runtime registry priority and reorders this volume in the manager registry only when the value changes. Higher weights are selected first before the 32-volume shader limit; equal weights keep stable registration order. Additive volumes are still compacted before regular volumes in the final shader upload. |
|`void SetColor(Color color)` | Sets volume color, updates the internal change cache and notifies the manager only when the value changes. |
|`void SetIntensity(float intensity)` | Sets volume intensity, updates the internal change cache and notifies the manager only when the value changes. |
|`void SetSmoothBlending(float radius)` | Calculates `InvLocalEdgeSmoothing` from the current lossy scale and radius. Notifies the manager only when the resulting smoothing data changes. |
|`void UpdateTransform()` | Recalculates `InvWorldMatrix`, `RelativeRotationRow0`, `RelativeRotationRow1` and `IsRotated`, then notifies the manager. Executes automatically from the manager for dynamic volumes when `AutoUpdateVolumes` is enabled. |

## PointLightVolumeInstance
Stores all runtime Point Light Volume configuration including light type, projection source, shadow source, runtime shadow bake settings, transform data, color and culling range.

When changing a Point Light Volume from another Udon script, prefer the setter methods below where they exist. Runtime shadow bake configuration is mostly controlled by public fields: assign the fields, then call `BakeShadows()`.

### Public Fields
| Public Field | Description |
| --- | --- |
|`bool IsDynamic` | Defines whether this point light volume can be moved in runtime. Disabling this option slightly improves CPU performance. |
|`int LightType` | Light type. `0` = Point Light, `1` = Spot Light, `2` = Area Light. |
|`Color Color` | Point light volume color. |
|`float Intensity` | Multiplies the color. Basically controls brightness. |
|`float ShadingStrength` | Controls per-surface Point Light Volume shading and shadow opacity based on surface normal. `0` disables this extra shading and shadows for the light, `1` applies them fully. |
|`Vector3 Position` | World-space position used by this point light volume. |
|`float LightSourceSize` | Light source size used by parametric point lights, parametric spot lights, cookies and cubemap projections. It affects calculated range and strongly affects size-aware specular width in modern compatible shaders. |
|`float InverseSquaredRange` | Inverse squared range used by LUT projection. |
|`float Width` | Area light width in meters. Affects textured Area Light emission and size-aware Area Light speculars in modern compatible shaders. |
|`Vector3 Direction` | World-space spotlight direction used by parametric and LUT spot lights. |
|`Quaternion Rotation` | Rotation used by area lights, cubemap projections and cookie projections. |
|`float ConeFalloff` | Spotlight cone falloff multiplier used by parametric spot lights. |
|`float Angle` | Half-angle of the spotlight cone, in radians. |
|`float OuterAngleCos` | Cosine of the spotlight outer angle used by parametric and LUT spot lights. |
|`float OuterAngleTan` | Tangent of the spotlight outer angle used by cookie projection and single-slice spot shadows. |
|`float SpotCookieAspect` | Width / height aspect used by custom Spot Light cookie projection. |
|`float Height` | Area light height in meters. Affects textured Area Light emission and size-aware Area Light speculars in modern compatible shaders. |
|`float AreaCookieMirror` | Internal Area Cookie X/Y reflection metadata derived from the transform, including reflected parent transforms. Do not set it manually; `UpdateRotation()`, `UpdateScale()` and the manager keep it synchronized. |
|`float SquaredRange` | Squared range after which the light is culled. Recalculated by the manager when `IsRangeDirty` is true. |
|`float SquaredScale` | Average squared lossy scale of the light. `LightSourceSize` gets multiplied by it at the end. |
|`LightVolumeManager LightVolumeManager` | Reference to the Light Volume Manager. Needed for runtime registration and updates. Assigning it after `Start()` / `OnEnable()` also registers the instance automatically. |
|`int RegistryOrder` | Current stable manager registry tie-breaker used when `RegistryWeight` values are equal. It is assigned automatically by the manager; read it for diagnostics or custom integrations, but do not set it manually. |
|`float RegistryWeight` | Current manager registry sort weight. Higher weights are selected first when active point lights exceed the shader limit. Read this field to get the current weight; use `SetWeight(float weight)` to change it so the manager can reorder the registry. |
|`bool IsActive` | Internal active state used by the manager. It becomes false when the GameObject is inactive, intensity is zero, or color is black. |
|`Texture CustomTexture` | Texture source used by this light's active LUT, cookie or cubemap projection. |
|`Material CustomTextureMaterial` | Material source used by this light's active LUT, cookie or cubemap projection. |
|`int ProjectionType` | Projection source type. `0` = none, `1` = texture, `2` = material. |
|`int ProjectionMode` | Projection mode. `0` = parametric, `1` = LUT, `2` = custom cookie or cubemap. |
|`bool AutoUpdateCustomTexture` | Updates this light's custom projection texture slice every frame when the manager's `AutoUpdateTextures` is enabled. |
|`bool CustomTextureIsCubemap` | Internal metadata. True when `CustomTexture` is a real cubemap source. |
|`bool CustomTextureHasDepthSlices` | Internal metadata. True when `CustomTexture` is a Texture2DArray or array RenderTexture with independent slices. |
|`Texture ShadowMapTexture` | Texture source used by this light's shadow map. |
|`Material ShadowMapMaterial` | Material source used by this light's shadow map. |
|`bool AutoUpdateShadowMap` | Updates this light's shadow map texture every frame when the manager's `AutoUpdateTextures` is enabled. |
|`float ShadowMapID` | Runtime shadow map ID used by the manager. `-1` means no shadow. |
|`bool WorldSpaceShadows` | Keeps baked shadows attached to their baked world-space pose instead of moving them with the light. Less optimized when enabled. |
|`Vector3 ShadowBakePosition` | World-space position where the shadow map was baked. |
|`Quaternion ShadowBakeRotation` | World-space rotation where the shadow map was baked. |
|`int LayerMask` | Layers that can cast shadows when using a runtime shadow baker. |
|`float NearClip` | Near clip plane used by both cubemap and single-slice shadow bake cameras and by the matching EVSM receiver depth range. |
|`float Bias` | World-space bias in meters applied while baking this light's shadow map. Larger values reduce self-shadow artifacts but can detach contact edges. |
|`float FarClip` | Far clip distance used by both cubemap and single-slice EVSM shadows. `0` recalculates it from this light's current culling range and is usually the recommended default. Use a manual value only to clip distant shadow casters or reduce the shadow depth range for a known bounded area. |
|`float Blur` | Shadow blur radius applied after baking, normalized to 128x128 shadow resolution. Editor baking uses spherical shadow-space blur to reduce visible cubemap and Spot Light projection seams. Runtime baking uses `Planar Blur` unless `PointLightShadowRuntimeBaker.SphericalBlur` is enabled. `0` keeps the baked shadow map unblurred. |
|`float ContactHardening` | Hardens shadows near contact areas. Can produce artifacts, so use it carefully. More performant when set to `0` in runtime shadow mode. Runtime baker spherical mode also applies to contact hardening samples. |
|`bool BakeInGame` | Bakes this light's shadow once from `Start()` in Play Mode or VRChat. The editor can still use a baked preview texture, but build/upload preprocessing clears that texture reference so it does not enter the build or asset bundle for this light. |
|`int RuntimeShadowResolution` | Resolution used by `BakeShadows()`. For `BakeInGame`, build/upload preprocessing normally sets it from **Light Volume Setup** shadow resolution. |
|`int RuntimeShadowBlurSamplePreset` | Runtime blur and contact hardening sample preset. `0` = Low, `1` = Medium, `2` = High, `3` = editor-quality internal preset. `BakeInGame` uses the highest normal runtime quality. |
|`bool RuntimeShadowSphericalBlur` | Enables spherical shadow-space runtime blur. `BakeInGame` enables this for better cubemap and single-slice Spot Light edge quality. |
|`int RuntimeShadowFacesPerFrame` | Number of cubemap faces processed per `BakeShadows()` trigger. Valid practical values are `1`, `2`, `3` and `6`; single-slice Spot Light shadows ignore this and bake one slice. `BakeInGame` uses one-frame full baking. |
|`bool RuntimeShadowDirectOutput` | Advanced realtime option. When true and the resolution matches the manager shadow atlas, `BakeShadows()` writes directly into the manager shadow texture array. The external realtime baker uses this to avoid keeping a full source texture per frame. |
|`bool ShadowMapTextureIsCubemap` | Internal metadata. True when `ShadowMapTexture` is a real cubemap source. |
|`bool ShadowMapTextureHasDepthSlices` | Internal metadata. True when `ShadowMapTexture` is a Texture2DArray or array RenderTexture with independent slices. |
|`bool ShadowMapUsesCubemap` | Internal metadata. True when the shadow source occupies 6 cubemap slices in the runtime shadow texture array. |
|`bool IsRangeDirty` | Flag that tells the manager to recalculate this light's culling range during the next update. |

### Public Methods
| Public Method | Description |
| --- | --- |
|`void SetDynamic(bool isDynamic)` | Sets dynamic mode and rebuilds the manager light list only when the value changes. |
|`void SetWeight(float weight)` | Sets runtime registry priority and reorders this point light volume in the manager registry only when the value changes. Higher weights are selected first before the 128-point-light shader limit; equal weights keep stable registration order. |
|`void SetLightSourceSize(float size)` | Sets light source size, or range data for LUT mode, then marks the range dirty only when the stored size/range data changes. For non-LUT lights this also changes size-aware specular width in modern compatible shaders. |
|`void SetLut()` | Sets this light into LUT projection mode and recalculates angle/rotation data. |
|`void SetCustomTexture()` | Sets this light into custom cookie or cubemap projection mode using the current source fields. |
|`void SetCustomTexture(Texture texture, bool isCubemap, bool autoUpdate)` | Assigns a texture projection source, sets projection metadata and optionally marks it for automatic runtime updates. The manager shares one runtime slice for matching source/update-mode pairs. |
|`void SetCustomMaterial(Material material, bool autoUpdate)` | Assigns a material projection source and optionally marks it for automatic runtime updates. The manager shares one runtime slice for matching material/update-mode pairs. |
|`void SetParametric()` | Sets this light into parametric projection mode if it is not already parametric. |
|`void SetPointLight()` | Sets this light into Point Light type, canonicalizes its shadow layout to cubemap, and updates position/rotation data only when needed. |
|`void SetSpotLight(float angleDeg, float falloff)` | Sets this light into Spot Light type with angle and falloff, updating only changed shader data. |
|`void SetSpotLight(float angleDeg)` | Sets this light into Spot Light type with angle only, updating only changed shader data. |
|`void SetAreaLight()` | Sets this light into Area Light type, canonicalizes its shadow layout to cubemap, and updates positive width/height, rotation and Area Cookie mirror data from the transform. |
|`void SetSpotCookieAspect(float aspect)` | Sets custom Spot Light cookie projection aspect and updates shader data. |
|`void SetColor(Color color)` | Sets light source color, updates the internal change cache and marks range dirty only when the value changes. |
|`void SetIntensity(float intensity)` | Sets light source intensity, updates the internal change cache and marks range dirty only when the value changes. |
|`void SetShadingStrength(float shadingStrength)` | Sets per-surface Point Light Volume shading and shadow strength in the `0..1` range, updating the internal change cache only when the value changes. |
|`void BakeShadows()` | Runs one native runtime shadow bake trigger using the current runtime shadow bake fields. It refreshes a dirty automatic range before resolving `FarClip`; changing the resolved Near/Far range restarts a partial cubemap cycle so all faces use identical depth encoding. Full one-frame baking happens when the light uses a single-slice Spot Light shadow or `RuntimeShadowFacesPerFrame` covers all required cubemap faces. |
|`void UpdateTransform()` | Updates position, rotation and scale data only when transform values changed. |
|`void UpdatePosition()` | Forces position data update and notifies the manager. |
|`void UpdateRotation()` | Forces rotation or direction data update and notifies the manager. |
|`void UpdateScale()` | Forces scale-dependent data update, recalculates area size when needed and marks range dirty. |

## PointLightShadowRuntimeBaker
Runtime Udon extension component from `Extra/Shadow Runtime Baker` that configures and triggers one **Point Light Volume Instance**. The actual runtime shadow bake is implemented in `PointLightVolumeInstance.BakeShadows()`. Use this component when a light needs rebaking on `OnEnable` or full realtime shadow updates.

The hidden camera and runtime materials are prepared automatically by the editor and build preprocessor. The `_RealtimeBakeLoop()` public event is internal to the delayed bake loop and should not be called manually.

Full realtime shadow baking is very expensive, usually more expensive than Unity realtime shadows. Prefer single-slice Spot Lights for realtime use, keep Spot Light angles below 180 degrees and preferably around 120 degrees or lower, and reserve realtime mode for heroic lights or single flashlights. Point Light and Area Light realtime shadows require cubemap updates and are much heavier.

### Public Fields
| Public Field | Description |
| --- | --- |
|`PointLightVolumeInstance TargetPointLightVolume` | Target point, spot or area light instance that receives the runtime-baked shadow texture. |
|`bool BakeOnEnable` | Configures the target Point Light Volume and triggers one full bake when this baker becomes active. Use it for enable-time rebakes. For simple startup-only baking, prefer `PointLightVolumeInstance.BakeInGame`. |
|`bool Realtime` | Continuously triggers target shadow bake calls through a delayed Udon event loop. Use carefully because full realtime shadow baking is expensive. |
|`int Resolution` | Resolution written into the target Point Light Volume before triggering its bake. Matching **Light Volume Setup** `Shadow Resolution` allows direct atlas output in realtime mode. |
|`int RealtimeFacesPerFrame` | Number of cubemap faces requested from the target per realtime bake tick. Single-slice Spot Light shadows ignore this and update one slice. |
|`int ShadowBlurSamplePreset` | Runtime blur and contact hardening sample preset. `0` = Low, `1` = Medium, `2` = High. `Planar Blur` uses 30/62/126 two-pass blur taps; `SphericalBlur` uses 33/65/129 one-pass blur taps. Lower presets are cheaper. |
|`bool SphericalBlur` | Samples runtime blur and contact hardening in spherical shadow space to reduce visible cubemap and single-slice Spot Light projection seams. More correct, but more expensive than `Planar Blur`. |

### Public Methods
| Public Method | Description |
| --- | --- |
|`void BakeShadows()` | Writes one-shot bake settings into `TargetPointLightVolume` and calls `TargetPointLightVolume.BakeShadows()`. It does not contain separate bake logic. |
