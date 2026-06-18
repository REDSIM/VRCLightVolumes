[VRC Light Volumes](../README.md) | **How to Use** | [Best Practices](../Documentation/BestPractices.md) | [Udon Sharp API](../Documentation/UdonSharpAPI.md) | [For Shader Developers](../Documentation/ForShaderDevelopers.md) | [Compatible Shaders](../Documentation/CompatibleShaders.md)

# How to Use

| Menu |
|----|
|[VRC Light Volumes System](../Documentation/HowToUse.md)|
|[Regular Light Volumes](../Documentation/HowToUse_RegularLightVolumes.md)|
|**Point Light Volumes**<br />• [Point Light Volumes Placement](#Point-Light-Volumes-Placement)<br />• [Light Projection](#Light-Projection)<br />• [Point Light Volume Component Description](#Point-Light-Volume-Component-Description)|
|[Point Light Volume Shadows](../Documentation/HowToUse_Shadows.md)|
|[Audio Link Integration](../Documentation/HowToUse_AudioLinkIntegration.md)|
|[TV Screens Integration](../Documentation/HowToUse_TVScreensIntegration.md)|
|[How Light Volumes Work?](../Documentation/HowToUse_HowItWorks.md)|

## Point Light Volumes

![](../Documentation/Preview_4.png)

**Point Light Volumes** is a fast and optimized custom lighting system that has it's own parametric Point Lights, Spot Lights and Area Lights. Point Light Volumes are not voxel based, they forms the light parametrically, or based on special LUT textures (similar to IES). They can project light cookies or cubemaps and can use baked or runtime-updated shadow maps. It can be up to 128 point lights visible in one scene at the same time.

**Point Light Volumes** consist of two components in the editor: `Point Light Volume` and `Point Light Volume Instance`.

The `Point Light Volume` component is an editor-only script that helps you configure the light more easily. It is not included in the VRChat upload. Its purpose is to set up the `Point Light Volume Instance` Udon script in a user-friendly way.

The `Point Light Volume Instance` component is a VRChat Udon script that stores all the data required by the Light Volumes system to render the light. You generally shouldn’t modify its values manually in the editor - use the `Point Light Volume` script instead. However, if you’re writing game logic that changes light parameters at runtime, you should reference the `Point Light Volume Instance` component, since it is the one that actually functions as the real light in-game.

## Point Light Volumes Placement

![](../Documentation/Preview_5.png)

**Point Light Volumes** are mostly useful in cases when you need independent dynamic lights, that can be individually toggled, moved or changed color in runtime.

If you just have a lot of point light sources that are static and don't change any of their properties in runtime, consider using a regular Light Volume and bake as much lights into it as you want. It is usually much more optimized than placing a lot of individual point lights. However, one Point Light Volume is usually much cheaper than a one regular additive light volume when you need runtime control.

Area Lights are a bit heavier than Point and Spot Lights, but they are not dramatically heavier anymore. You can safely use them for movable and scalable runtime soft boxes. Just avoid excessive overlaps, and still prefer baking a regular Light Volume in a shape of an area light when the light is fully static.

Note that more point lights you have active in your scene, the less performance you'll have. So, consider manually turning off unused point lights if you have a lot of them at your scene.

The **more** point light volumes overlap, the **less** performance you'll have! 

**Point light Volumes** calculates the **range** automatically based on their `Light Source Size` value, their scale, `Intensity` and `Color`. You can also configure the `Brightness Cutoff` value in the **Light Volume Setup** to limit the effective range of the light and improve performance. Higher values reduce the light's visible radius, which generally increases performance, but results in less realistic light attenuation.

Try not to make an insanely huge range for your lights. Use `Debug Range` flag in your Point Light Volume component to preview the region affected by your point light.

If a static Point Light Volume should also affect avatars or props with no Light Volumes shader support, enable `Bake Into Probes` before baking. This bakes the point light contribution into regular Unity Light Probes. It is not needed for objects using shaders with VRC Light Volumes support.

## Light Projection

### Parametric

Point Light Volumes and Spot Light Volumes use `Parametric` projection by default. **Point Light Volumes work differently compared to Unity’s built-in lights.** They use inverse-square light attenuation that more closely resembles how light behaves in the real world.

![](../Documentation/Preview_7.png)

The main difference to Unity’s built-in lights is the `Light Source Size` property. It represents the physical radius of the light-emitting surface, like a matte light bulb for point lights, or a flashlight reflector for spotlights.

Note that `Intensity` can be very high (in the hundreds or even thousands) for small `Light Source Size` values. This is because intensity here represents the light emitted per unit of surface area. A smaller light source must emit more intense light to achieve a reasonable visible range.

> [!TIP]
> Scaling the light game object also scales the light source size!

In Spot Light mode, several additional parametric shape properties are available. The `Angle` property controls the cone angle of the spotlight in degrees. Unlike Unity’s built-in Spot Light, this angle can exceed 180 degrees to create an inverted cone. The `Falloff` property adjusts the softness of the cone edges.

### LUT

If you want to create a complex light shape and attenuation, `LUT` projection is what you need. For the Spot Light mode, LUT works similar to IES light shape format, but easier for people to create their own LUT presets.

![](../Documentation/Preview_6.png)

**LUT** (Look Up Table) texture data in horizontal direction describes light color change from the center of the spot light cone to the cone edge. Vertical direction of the texture data describes the light attenuation, that is usually should be an inversed square distribution, but you can make it linear or anything else if you want to create any special light effects.

In Point Light mode, only vertical texture direction is used, as there are no cone. Horizontal data will just be ignored.

So, LUT is the only projection mode, which can customize the light attenuation. It uses `Range` property to manually define the light range.

> [!IMPORTANT]
> It’s recommended to completely disable compression for any texture used as a Cookie or a LUT. The Light Volumes system does not inherit the compression settings, but compression artifacts will still remain and affect the result.

### Custom

If you want just to project a light cookie texture, you can use `Custom` projection mode. Unlike Unity’s built-in Spot Light, here cookie can project a colored texture, that can work as a projector. Using angle with more than 180 degrees will not create an inversed cone in this case.

![](../Documentation/Preview_8.png)

Point Light in `Custom` projection mode can project a cubemap instead of a regular cookie. So it's a perfect solution to make disco balls, lamps that projects stars or anything else you want.

### Projection Texture Resolution

When you assign a LUT, Cookie texture, Cubemap, Render Texture, or Material, the **Light Volumes** system automatically packs everything into a shared runtime **Texture Array**. The `Cookie Resolution` of this array can be configured in the **Light Volumes Setup** component.

> [!WARNING]
> High resolutions increase VRAM usage and can cause temporary lag while the texture array is rebuilt.

LUTs and Cookie textures share the same resolution, as they are packed into the same texture array. Cubemaps, however, require 6 slices per entry (one for each face), so each cubemap takes up six times more space than a LUT or Cookie. If your input textures have a different resolution, they will be automatically rescaled during packing. 

Duplicated LUTs, Cubemaps, and Cookie textures are only uploaded to VRChat once and are reused by all lights that reference them. So don’t worry about using the same textures across multiple Point Light Volumes - it won’t increase the build size.

If you use a `RenderTexture` or a `Material` as the source, the shared texture array can be updated in runtime. This is controlled by `Auto Update Textures` in **Light Volume Setup**. Keep it disabled if all projection sources are static textures.

> [!IMPORTANT]
> It’s recommended to completely disable compression for any texture used as a Cookie or a LUT. The Light Volumes system does not inherit the compression settings, but compression artifacts will still remain and affect the result.

### Recommended Source Texture Formats:

- **`RGBA32`** – The lightest format, but it does **not** support HDR. Not recommended for LUTs, as it causes visible banding artifacts.
- **`RGBA Half`** – The recommended format for most cases. Supports HDR and works well with LUTs. It uses half precision, so minimal banding may still be visible, but usually unnoticeable.
- **`RGBA Float`** – The highest quality format with full HDR support and no banding. It’s also the most memory-heavy and is typically overkill for general use.

For shadow setup, baked shadows, the Realtime Shadow Baker and runtime script control, see [Point Light Volume Shadows](../Documentation/HowToUse_Shadows.md).

## Point Light Volume Component Description

| Parameter | Description |
| --- | --- |
|`Dynamic` | Defines whether this point light volume can be moved in runtime. Disabling this option slightly improves performance on the CPU side. If you want to make Dynamic lights auto-update their positions and other parameters in runtime, enable **Auto Update Volumes** in **Light Volume Setup**, or call the **UpdateVolumes()** function manually through an Udon script. Otherwise, they will stay in one place in game.|
|`Type` | Changes the light mode between Point Light, Spot Light and Area Light.|
|`Light Source Size` | Physical radius of a light source if it was a matte glowing sphere for a point light, or a flashlight reflector for a spot light. Larger size emits more light without increasing overall intensity.|
|`Range` | Radius in meters beyond which point and spot lights are culled. (Only available in LUT light shape mode)|
|`Color` | Multiplies the point light volume’s color by this value.|
|`Intensity` | Brightness of the point light volume.|
|`Shading Strength` | Controls normal masking and shadow opacity for this light. Lower values are cheaper and softer. `0` disables this extra shading.|
|`Bake Into Probes` | Bakes this Point Light Volume into Unity Light Probes. Useful for static lights that should affect objects without Light Volumes shader support.|
|`Debug Range` | Shows overdrawing range gizmo. Less point light volumes intersections - more performance!|
|`Projection` | Parametric uses settings to compute light falloff. LUT uses a texture: X - cone falloff, Y - attenuation (Y only for point lights). Cookie projects a texture for spot lights. Cubemap projects a cubemap for point lights.|
|`Angle` | Angle of a spotlight cone in degrees. (Only available in spotlight mode)|
|`Falloff` | Spotlight cone falloff. (Only available in parametric spotlight mode)|
|`Falloff LUT` | Texture that defines custom light shape. Similar to IES. X - cone falloff, Y - attenuation. No compression and RGBA Float or RGBA Half format is recommended.|
|`Cookie` | Projects a square texture, RenderTexture or Material for spot lights.|
|`Cubemap` | Projects a texture, Cubemap, Texture2DArray, RenderTexture, or Material for point lights. Cubemap and array sources use independent faces; a single 2D texture is copied to all faces.|
|`Shadows` | Enables shadow map sampling for this light. Requires a baked or assigned shadow source.|
|`Shadow Map` | Shadow texture source used by this light. Can be generated by `Bake Shadows`, assigned manually, or updated by the runtime baker.|
|`Layer Mask` | Layers that can cast shadows during shadow baking.|
|`Object Mask` | Optional object list. If empty, all objects on the selected layers can cast shadows. If not empty, only children of the listed objects are rendered during the bake.|
|`Near Plane` | Near clip plane used by the shadow bake camera. Higher values can clip nearby occluders.|
|`Bias` | World-space bias in meters used while baking shadows. Larger values reduce self-shadow artifacts but can detach contact edges.|
|`Blur` | Shadow blur radius applied after baking, normalized to 128x128 shadow resolution. Editor baking uses spherical shadow-space blur to reduce visible cubemap and Spot Light projection seams. Runtime baking uses `Planar Blur` unless `Spherical Blur` is enabled on the runtime baker. `0` keeps shadows unblurred.|
|`Contact Hardening` | Hardens shadows near contact areas. Can produce artifacts, so use it carefully. More performant when set to `0` in runtime shadow mode. Runtime baker `Spherical Blur` also applies to contact hardening samples.|
|`Use World Space` | Keeps baked shadows attached to the baked world-space pose instead of moving them with the light. Less optimized when enabled.|
|`Force Cubemap Shadows` | Forces spotlight shadows to bake and store as a cubemap even when the spot angle could use a single projected shadow texture.|
|`Rebake Shadows` | Includes this light when pressing `Bake Shadows` in **Light Volume Setup**.|
