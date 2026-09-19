[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [Scripting API](./ScriptingAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Point Light Volume Shadows

| Menu |
| --- |
| [Overview](./HowToUse.md) |
| [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) |
| [Point Light Volumes](./HowToUse_PointLightVolumes.md) |
| [Froxel Clustering](./HowToUse_FroxelClustering.md) |
| **Shadows**<br />• [Baked Shadows](#baked-shadows)<br />• [Penumbra And Blur](#penumbra-and-blur)<br />• [PC And Quest](#pc-and-quest)<br />• [Bake In Game](#bake-in-game)<br />• [Realtime Shadows](#realtime-shadows)<br />• [Bake Shadows Via Script](#bake-shadows-via-script)<br />• [Keep It Performant](#keep-it-performant) |
| [Material Sources](./HowToUse_PointLightMaterialSources.md) |
| [Area Light Emission](./HowToUse_AreaLightEmission.md) |
| [AudioLink](./HowToUse_AudioLinkIntegration.md) |
| [TV Screens (Older Workflow)](./HowToUse_TVScreensIntegration.md) |
| [Debugging](./HowToUse_Debugging.md) |
| [How It Works](./HowToUse_HowItWorks.md) |

Point, Spot and Area lights can cast shadows onto surfaces with a [shader that supports VRC Light Volumes](./CompatibleShaders.md). Baked shadows work on moving props and avatars. Bake again when the light or a shadow-casting object moves.

![Point and Spot Light Volumes casting shadows from columns and a lamp frame.](./Preview_9.png)

## Baked Shadows

Enable **Shadows > Enabled** on the light and click **Bake Shadows**. The resulting shadow map captures the current geometry; changing the light's color or brightness does not need another bake.

The light's **Shadows** section contains these settings:

| Setting | What it does |
| --- | --- |
| **Enabled** | Enables shadows for this light. |
| **Use World Space** | Keeps the shadow at its baked position and rotation. When off, the shadow moves with the light. Rebake when it needs to match changed geometry. |
| **Layer Mask** | Selects which layers can cast shadows. Exclude layers that do not need to block the light. |
| **Excluded Renderers** | Excludes specific Renderers from the bake, such as a lamp's emitting surface. Listing a parent does not exclude its children. |
| **Force Cubemap Shadows** | Captures six directions for a Spot light. Consider it for wide angles around 120°; enable it at 180° or more to cover the full cone. |
| **Bias** | Offsets the captured depth to reduce self-shadowing artifacts. Too much makes shadows appear detached from objects. |
| **Near Plane** | Closest distance included in the capture. Geometry closer to the light is excluded. |
| **Far Plane** | Farthest distance included in the capture. `0 (Auto)` uses the light's range. Set it manually if the light starts at zero brightness. |
| **Debug Clip Planes** | Shows the near and far capture limits in the Scene view. |
| **Blur** | Softens shadow edges. See [Penumbra And Blur](#penumbra-and-blur). |
| **Contact Hardening** | Approximates sharper shadows near contact with the object casting them. |
| **Spherical Blur** | Blurs across cubemap face boundaries to reduce seams. |
| **Bake In Game** | Bakes once when the light first starts in game, instead of including its Editor-baked map in the world build. |
| **Quality** | Blur and Contact Hardening quality for in-game bakes, including Realtime updates. Editor bakes use a separate, higher-quality preset. |
| **Shadow Map** | The texture created by a bake. It can also accept a custom shadow texture or Material. |
| **Resolution** | Resolution used to capture this light's shadow. **Manager** inherits the Manager's **Shadow Resolution**. |
| **Rebake Shadows** | Includes this light when **Bake Shadows** is clicked on the Manager. Turn it off to preserve this map during a batch bake. |

Rebake after moving the light or shadow-casting geometry, or changing capture and blur settings. **Clear Shadows** removes the assigned map without deleting its source asset.

## Penumbra And Blur

The **penumbra** is the soft edge between light and shadow. Light Volumes creates it by blurring the shadow map. Increase **Blur** for softer edges, or reduce it for sharper shadows. Rebake to see the change.

For bakes performed in game, **Quality** controls how thoroughly the blur is sampled. Higher quality can make wide, low-quality blur look smoother, but costs more to calculate. The setting also affects **Contact Hardening**.

**Spherical Blur** can help when you see seams between cubemap faces. Its sampling pattern can produce a rougher-looking blur than the regular filter, so compare both and keep the result you prefer.

**Contact Hardening** approximates a sharp shadow near the object casting it, with a softer edge farther away. This is a visual approximation, not a physically accurate penumbra. It can produce artifacts such as doubled edges or split penumbras. Use it where the result looks good; reduce it or set it to `0` where it does not. It also adds work to the bake.

## PC And Quest

Quest and other mobile devices can show shadow artifacts that are not visible on PC. Two settings on the Manager help balance stability and light leaking:

| Setting | What it does |
| --- | --- |
| **Shadow Min Variance** | Reduces artifacts caused by limited numerical precision. Higher values can let light leak into shadowed areas. |
| **Shadow Bleed Reduction** | Suppresses that light leaking. Higher values darken and sharpen shadows, but can remove soft detail. |

**Shadow Min Variance** at `1` and **Shadow Bleed Reduction** around `0.2–0.4` often fix mobile artifacts. Some light leaking may remain; adjust both to find a balance between stable shadows, soft edges and leaking that looks right in your scene. These settings update without rebaking.

## Bake In Game

Enable **Bake In Game** on the light to bake once when it first starts or becomes active. Re-enabling it later does not bake again.

You can still use **Bake Shadows** in the Editor for a preview. The Editor-baked map is left out of the world build, but the runtime map still uses GPU memory. The Manager processes one light per frame; a Point or Area light captures all six faces in that frame, so a bake can cause a brief stutter.

Keep the light and Manager active until the bake finishes. For spawned lights, assign the Manager before their first activation. Use [Bake Shadows Via Script](#bake-shadows-via-script) for later changes or retries.

## Realtime Shadows

**Point Light Shadow Runtime Baker** updates a light's shadow while the world runs. Use it when the light or objects casting its shadow move. Its **Realtime** mode captures a new shadow every frame; **Bake On Enable** captures one each time the baker is activated.

1. Add **Point Light Shadow Runtime Baker** and assign **Target Point Light Volume**.
2. Enable shadows on the target light and set its capture and blur parameters.
3. Enable **Realtime** for continuous updates, or **Bake On Enable** for activation-time bakes. Realtime takes priority.
4. For a moving light, also enable its **Dynamic** and the Manager's **Auto Update Volumes**.
5. Turn off the target light's **Bake In Game** when the runtime baker handles its startup. Check the result in Play Mode.

> [!WARNING]
> Realtime shadows are expensive. Reserve them for a couple of Spot lights with **Angle below 180°** and **Force Cubemap Shadows off**. Point, Area and cubemap Spot shadows capture **six faces instead of one** every update: six times as many scene captures, plus shadow processing.

After stopping automatic updates, you can refresh the shadow once by calling `BakeShadows()` on your assigned baker, here named `ShadowBaker`:

```csharp
ShadowBaker.BakeShadows();
```

This call does not restart automatic updates. To resume Realtime in VRChat, turn **Realtime** on, then disable and re-enable the baker component while its GameObject is active. Setting Realtime back on by itself does not restart a stopped Udon update loop.

## Bake Shadows Via Script

Scripted bakes are useful in interactive worlds where players rearrange furniture or other objects. Bake once after an object is placed to update its shadow without paying for continuous Realtime updates.

With an active light assigned as `Lamp`, call its `PointLightVolumeInstance.BakeShadows()` after the geometry changes:

```csharp
Lamp.BakeShadows();
```

The light needs **Shadows** enabled and a Manager assigned. Before building, enable **Bake In Game** or assign the light to a **Point Light Shadow Runtime Baker** so its runtime bake resources are prepared. See the [UdonSharp API](./ScriptingAPI.md#runtime-shadow-baking) for the full contract.

> [!IMPORTANT]
> If **Shader Stripping** is enabled and the scene has no authored lights with shadows, automatic detection can remove shadow support needed by your scripts. Before building, turn off **Auto** in the Manager's Shader Stripping settings and retain **Shadows**, the required light types, and **Single-slice Shadows** or **Cubemap Shadows** as needed. Retain **World Space Shadows** too if you use it.

You can also supply animated shadows from a **Render Texture** or **Material** through **Shadow Map** for custom effects. Keep the Manager's **Auto Update Textures** enabled for live updates. The source must use the expected shadow data format; an ordinary black-and-white mask or camera-depth texture will not work. See the [shadow source requirements and example](./TechnicalDetails.md#shadow-map-materials).

## Keep It Performant

Shadows add rendering cost. Keep the number of shadowed lights low, and prefer baked shadows for lights and geometry that stay still.

- **Keep resolution low.** Shadows often need less detail than a normal texture. Use the lowest Manager **Shadow Resolution** that looks acceptable. A light's **Resolution** controls its bake resolution; the Manager sets the shared resolution used in game.
- **Keep in-game blur quality low.** Use the lowest **Quality** that gives acceptable edges. A lower-resolution map can already soften the shadow enough to need little blur. Increasing resolution without increasing blur quality can make the penumbra look less smooth.
- **Limit what gets captured.** Use **Layer Mask** and **Excluded Renderers** to leave unnecessary geometry out of runtime bakes.
- **Use [Shadow Culling (Hi-Z)](./HowToUse_FroxelClustering.md#shadow-culling-hi-z) with Froxel Clustering** when walls, floors or ceilings block large parts of many lights' ranges. It can skip lighting calculations in those shadowed areas, such as other rooms or floors.
- **Keep Realtime updates rare.** They require repeated scene captures and shadow filtering and can cost more than Unity's built-in realtime shadows. Use as few as possible, preferably narrow Spot lights, and check frame time on the target device.
