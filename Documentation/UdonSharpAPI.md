[VRC Light Volumes](../README.md) | [How to Use](../Documentation/HowToUse.md) | [Best Practices](../Documentation/BestPractices.md) | **Udon Sharp API** | [For Shader Developers](../Documentation/ForShaderDevelopers.md) | [Compatible Shaders](../Documentation/CompatibleShaders.md)

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
|`int ShadowTextureFormat` | Precision used for EVSM shadow maps and the runtime shadow texture array. `0` = Half, `1` = Float. |
|`bool LightProbesBlending` | When enabled, areas outside Light Volumes fall back to Unity Light Probes. Otherwise, the Light Volume with the smallest weight is used as fallback. It also improves performance. |
|`bool SharpBounds` | Disables smooth blending with areas outside Light Volumes. Use it if your entire scene's play area is covered by Light Volumes. It also improves performance. |
|`bool AutoUpdateVolumes` | Automatically updates transform data for volumes marked `IsDynamic`. Enabling/disabling, `Color` and `Intensity` changes update without this option. |
|`bool AutoUpdateTextures` | Automatically updates dynamic Point Light Volume cookie, LUT, cubemap and shadow sources, such as RenderTextures or Materials. |
|`int AdditiveMaxOverdraw` | Limits the maximum number of additive Light Volumes and Point Light Volumes that can affect a single pixel. |
|`bool ForceSceneLighting` | Disables min/max brightness limits for modern avatar shaders such as lilToon or Poiyomi. Enable only if you're sure your scene lighting is properly configured. |
|`LightVolumeInstance[] LightVolumeInstances` | All registered Light Volume instances. You can enable or disable volume GameObjects at runtime. Manually disabling unnecessary volumes improves performance. |
|`PointLightVolumeInstance[] PointLightVolumeInstances` | All registered Point Light Volume instances. You can enable or disable point light GameObjects at runtime. Manually disabling unnecessary point lights improves performance. |
|`RenderTexture CustomTextures` | Runtime texture array used for Point Light Volume cubemaps, LUTs and cookies. Cubemap faces are stored first, 6 slices per cubemap. |
|`int CubemapsCount` | Number of cubemaps stored in `CustomTextures`. Cubemap faces start from the beginning, 6 elements per cubemap. |
|`bool HasAutoCustomTextureUpdates` | Internal state. True when at least one projection source needs per-frame texture updates. |
|`RenderTexture ShadowTextures` | Runtime texture array used for Point Light Volume shadow maps. |
|`int ShadowCubemapsCount` | Number of cubemap shadow maps stored in `ShadowTextures`. Cubemap shadow maps use 6 slices each and are stored first. |
|`int ShadowMapsCount` | Total shadow map count stored in `ShadowTextures`. Cubemap shadows use 6 array slices, single projected shadows use 1 slice. |
|`bool HasAutoShadowTextureUpdates` | Internal state. True when at least one shadow source needs per-frame texture updates. |
|`Material CubemapFaceMaterial` | Internal material used to copy cubemap faces into runtime texture arrays. You usually don't need to touch this field manually. |

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
|`void InitializeLightVolume(LightVolumeInstance lightVolume)` | Registers a Light Volume instance at runtime. Called automatically by `LightVolumeInstance.Start()` / `OnEnable()` when `LightVolumeManager` is assigned. |
|`void DeinitializeLightVolume(LightVolumeInstance lightVolume)` | Removes a Light Volume instance from the runtime registry without resizing the array. Called automatically on disable. |
|`void InitializePointLightVolume(PointLightVolumeInstance pointLightVolume)` | Registers a Point Light Volume instance at runtime. Called automatically by `PointLightVolumeInstance.Start()` / `OnEnable()` when `LightVolumeManager` is assigned. |
|`void DeinitializePointLightVolume(PointLightVolumeInstance pointLightVolume, bool customTexturesChanged, bool shadowTexturesChanged)` | Removes a Point Light Volume instance from the runtime registry and optionally invalidates projection or shadow texture caches. Called automatically on disable. |
|`void ReinitializeCustomTextures()` | Rebuilds the shared runtime texture array for Point Light Volume LUTs, cookies and cubemaps. Call this after changing projection sources manually. |
|`void UpdateAutoCustomTextures()` | Updates only projection sources marked for per-frame refresh. Usually called automatically when `AutoUpdateTextures` is enabled. |
|`void ReinitializeShadowTextures()` | Rebuilds the shared runtime texture array for Point Light Volume shadow maps. Call this after changing shadow sources manually. |
|`void UpdateAutoShadowTextures()` | Updates only shadow sources marked for per-frame refresh. Usually called automatically when `AutoUpdateTextures` is enabled. |
|`void UpdatePointLightShadowTextureSlice(PointLightVolumeInstance instance, int sourceSlice)` | Copies one shadow source slice into the shared shadow texture array. Runtime shadow bakers use this when they manage their own update loop. |
|`int GetPointLightCustomID(PointLightVolumeInstance instance)` | Returns the resolved projection texture ID for a Point Light Volume instance, or `-1` if none is assigned. |
|`void RequestUpdateVolumes()` | Schedules a Light Volume data update on the next delayed update tick. Prefer this over calling `UpdateVolumes()` repeatedly. |
|`void UpdateProcess()` | Internal delayed Udon event that processes scheduled volume and texture updates. Usually don't call it manually. |
|`void UpdateVolumes()` | Immediately rebuilds and uploads all Light Volume and Point Light Volume shader data. Useful when you intentionally manage updates manually instead of relying on delayed requests. |

## LightVolumeInstance
Stores all runtime regular Light Volume configuration including atlas UVW data, transform data, color and intensity.

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
|`LightVolumeManager LightVolumeManager` | Reference to the Light Volume Manager. Needed for runtime registration and updates. |
|`bool IsActive` | Internal active state used by the manager. It becomes false when the GameObject is inactive, intensity is zero, or color is black. |

### Public Methods
| Public Method | Description |
| --- | --- |
|`void _onVarChange_Color()` | Internal Udon event used to detect direct color changes on the UdonBehaviour. Usually don't call it manually. |
|`void _onVarChange_Intensity()` | Internal Udon event used to detect direct intensity changes on the UdonBehaviour. Usually don't call it manually. |
|`void SetSmoothBlending(float radius)` | Calculates `InvLocalEdgeSmoothing`. Execute it if you want to control edge smoothing in runtime. |
|`void UpdateTransform()` | Recalculates `InvWorldMatrix`, `RelativeRotationRow0`, `RelativeRotationRow1` and `IsRotated`, then notifies the manager. Executes automatically from the manager for dynamic volumes when `AutoUpdateVolumes` is enabled. |

## PointLightVolumeInstance
Stores all runtime Point Light Volume configuration including light type, projection source, shadow source, transform data, color and culling range.

### Public Fields
| Public Field | Description |
| --- | --- |
|`bool IsDynamic` | Defines whether this point light volume can be moved in runtime. Disabling this option slightly improves CPU performance. |
|`int LightType` | Light type. `0` = Point Light, `1` = Spot Light, `2` = Area Light. |
|`Color Color` | Point light volume color. |
|`float Intensity` | Multiplies the color. Basically controls brightness. |
|`float ShadingStrength` | Controls normal masking and shadow opacity based on surface normal. `0` disables this extra shading, `1` applies it fully. |
|`Vector3 Position` | World-space position used by this point light volume. |
|`float LightSourceSize` | Light source size used by parametric point lights, parametric spot lights, cookies and cubemap projections. |
|`float InverseSquaredRange` | Inverse squared range used by LUT projection. |
|`float Width` | Area light width in meters. |
|`Vector3 Direction` | World-space spotlight direction used by parametric and LUT spot lights. |
|`Quaternion Rotation` | Rotation used by area lights, cubemap projections and cookie projections. |
|`float ConeFalloff` | Spotlight cone falloff multiplier used by parametric spot lights. |
|`float Angle` | Half-angle of the spotlight cone, in radians. |
|`float OuterAngleCos` | Cosine of the spotlight outer angle used by parametric and LUT spot lights. |
|`float OuterAngleTan` | Tangent of the spotlight outer angle used by cookie projection and single-slice spot shadows. |
|`float Height` | Area light height in meters. |
|`float SquaredRange` | Squared range after which the light is culled. Recalculated by the manager when `IsRangeDirty` is true. |
|`float SquaredScale` | Average squared lossy scale of the light. `LightSourceSize` gets multiplied by it at the end. |
|`LightVolumeManager LightVolumeManager` | Reference to the Light Volume Manager. Needed for runtime registration and updates. |
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
|`float NearClip` | Near clip plane used by the shadow bake camera. |
|`float Bias` | World-space bias in meters applied while baking this light's shadow map. Larger values reduce self-shadow artifacts but can detach contact edges. |
|`float FarClip` | Far clip distance used when the EVSM shadow map was baked. `0` falls back to this light's current culling range. |
|`float Blur` | Gaussian blur radius applied after baking, normalized to 128x128 shadow resolution. `0` keeps the baked shadow map unblurred. |
|`float ContactHardening` | Hardens shadows near contact areas. Can produce artifacts, so use it carefully. More performant when set to `0` in runtime shadow mode. |
|`bool ShadowMapTextureIsCubemap` | Internal metadata. True when `ShadowMapTexture` is a real cubemap source. |
|`bool ShadowMapTextureHasDepthSlices` | Internal metadata. True when `ShadowMapTexture` is a Texture2DArray or array RenderTexture with independent slices. |
|`bool ShadowMapUsesCubemap` | Internal metadata. True when the shadow source occupies 6 cubemap slices in the runtime shadow texture array. |
|`bool IsRangeDirty` | Flag that tells the manager to recalculate this light's culling range during the next update. |

### Public Methods
| Public Method | Description |
| --- | --- |
|`void _onVarChange_Color()` | Internal Udon event used to detect direct color changes on the UdonBehaviour. Usually don't call it manually. |
|`void _onVarChange_Intensity()` | Internal Udon event used to detect direct intensity changes on the UdonBehaviour. Usually don't call it manually. |
|`void _onVarChange_ShadingStrength()` | Internal Udon event used to detect direct shading strength changes on the UdonBehaviour. Usually don't call it manually. |
|`void SetLightSourceSize(float size)` | Sets light source size, or range data for LUT mode, then marks the range dirty. |
|`void SetLut()` | Sets this light into LUT projection mode and recalculates angle/rotation data. |
|`void SetCustomTexture()` | Sets this light into custom cookie or cubemap projection mode using the current source fields. |
|`void SetCustomTexture(Texture texture, bool isCubemap, bool autoUpdate)` | Assigns a texture projection source, sets projection metadata and optionally marks it for automatic runtime updates. |
|`void SetCustomMaterial(Material material, bool autoUpdate)` | Assigns a material projection source and optionally marks it for automatic runtime updates. |
|`void SetParametric()` | Sets this light into parametric projection mode. |
|`void SetPointLight()` | Sets this light into Point Light type and updates position/rotation data. |
|`void SetSpotLight(float angleDeg, float falloff)` | Sets this light into Spot Light type with angle and falloff. |
|`void SetSpotLight(float angleDeg)` | Sets this light into Spot Light type with angle only. |
|`void SetAreaLight()` | Sets this light into Area Light type and updates width, height and rotation data from the transform. |
|`void SetColor(Color color)` | Sets light source color and marks range dirty. |
|`void SetIntensity(float intensity)` | Sets light source intensity and marks range dirty. |
|`void SetShadingStrength(float shadingStrength)` | Sets normal masking and shadow strength in the 0..1 range. |
|`void UpdateTransform()` | Updates position, rotation and scale data only when transform values changed. |
|`void UpdatePosition()` | Forces position data update and notifies the manager. |
|`void UpdateRotation()` | Forces rotation or direction data update and notifies the manager. |
|`void UpdateScale()` | Forces scale-dependent data update, recalculates area size when needed and marks range dirty. |

## PointLightShadowRuntimeBaker
Runtime Udon component from `Extra/Shadow Runtime Baker` that renders EVSM shadow maps for one **Point Light Volume Instance**. Use it when a shadow needs to update in runtime.

The hidden camera and runtime materials are prepared automatically by the editor and build preprocessor, so they are intentionally not listed here as regular user-facing fields. The `_RealtimeBakeLoop()` public event is internal to the delayed bake loop and should not be called manually.

### Public Fields
| Public Field | Description |
| --- | --- |
|`PointLightVolumeInstance TargetPointLightVolume` | Target point, spot or area light instance that receives the runtime-baked shadow texture. |
|`bool BakeOnEnable` | Runs one distributed bake cycle when the baker becomes active. |
|`bool Realtime` | Continuously updates shadow slices through a delayed Udon event loop. Use carefully because realtime shadow baking is expensive. |
|`int Resolution` | Resolution used by the runtime depth target and shadow texture. Matching **Light Volume Setup** `Shadow Resolution` avoids an extra copy path. |
|`int RealtimeFacesPerFrame` | Number of cubemap faces rendered per realtime bake tick. Single-slice Spot Light shadows ignore this and update one slice. |
|`int ShadowBlurSamplePreset` | Runtime blur sample preset. `0` = Low, `1` = Medium, `2` = High. Lower presets are cheaper. |

### Public Methods
| Public Method | Description |
| --- | --- |
|`void BakeShadows()` | Bakes all shadow slices immediately. If `Realtime` is enabled, it also starts the realtime bake loop. |
