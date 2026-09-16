[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [UdonSharp API](./UdonSharpAPI.md) | [Unity Editor API](./UnityEditorAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Point Light Volume Shadows

**Guides:** [Overview](./HowToUse.md) · [Point Light Volumes](./HowToUse_PointLightVolumes.md) · [Froxel Clustering](./HowToUse_FroxelClustering.md) · **Shadows** · [Material Sources](./HowToUse_PointLightMaterialSources.md) · [Area Light Emission](./HowToUse_AreaLightEmission.md)

Point, Spot and Area Light Volumes can cast shadows onto surfaces using a current compatible shader. A baked shadow can fall on a moving prop or avatar, but it only contains the objects captured when it was baked. New or moving blockers need a new shadow bake.

![Point and Spot Light Volumes casting shadows from columns and a lamp frame.](./Preview_9.png)

Look at the long column shadows across the floor and the frame-shaped shadow around the orange lamp on the right. These shadows come from geometry captured from each light's position.

## Choose A Shadow Mode

| Mode | Good use | Cost while playing |
| --- | --- | --- |
| No shadows | Decorative lights with no visible blockers. | Lowest. |
| **Bake Shadows** in the Editor | A fixed lamp and stationary walls/furniture. The light may still change color or brightness. | Shadow texture memory and sampling. |
| **Bake In Game** | Geometry should be captured once after the world or object starts. Saves shipping the editor-baked shadow texture. | One runtime bake, then texture memory and sampling. |
| Runtime baker **Bake On Enable** | A light needs fresh shadows each time its baker is activated. | One bake per activation. |
| Runtime baker **Realtime** | A moving flashlight or moving shadow caster. | Continuous camera renders and shadow processing. |

Use editor-baked shadows for fixed lights. Use continuous updates only where the changing shadow is visible and worth the cost.

## Bake A Fixed Lamp

1. Select the **Point Light Volume**. Under **Shadows**, turn on **Enabled**.
2. Leave **Resolution** at **Manager - …** to inherit the Manager's **Shadow Resolution**, or choose a per-light resolution.
3. Set **Layer Mask** to the layers that should cast the shadow.
4. Add any individual objects to **Excluded Renderers** if they should not block this light. Drag their Renderer components; adding a parent does not exclude all its children. For example, exclude the bulb mesh or screen surface if it blocks its own light.
5. Leave **Far Plane** at `0 (Auto)` initially. Enable **Debug Clip Planes** to see the camera's near/far limits.
6. Press **Bake Shadows**. Check the wall or floor behind a blocker: its shadow should now be visible.
7. Adjust **Bias** if surfaces shadow themselves, or **Blur** for softer edges, then bake again.

Bake again after moving the light or blockers, or changing the caster mask, clip planes, Bias, Blur, Contact Hardening or Spherical Blur. These settings affect the generated shadow texture.

![Diagram: a moving receiver can use a fixed baked shadow; a moving blocker needs a new capture.](./Images/shadow-updates.svg)

Move a test prop with a compatible material through the baked shadow. Its lighting should change without another bake, as long as the light and the captured blockers stay fixed.

The Manager's **Bake Shadows** button processes lights with shadows enabled and **Rebake Shadows** checked. Uncheck Rebake Shadows to keep one light's existing map during a batch bake. **Clear Shadows** on a light removes its assigned map without deleting the source asset.

## Get The Shadow Coverage Right

A normal **Spot Light** uses one projected shadow view. **Point** and **Area Lights** use six cubemap views. **Force Cubemap Shadows** also makes a Spot use six views.

For a flashlight, use a Spot and keep its angle reasonably narrow. This gives the shadow texture more useful detail and avoids six camera renders per update. At Spot angles of 180 degrees or more, enable **Force Cubemap Shadows**; the single-view projection cannot cover them.

**Near Plane** clips objects too close to the light. Keep it small enough to include nearby blockers. **Far Plane** clips distant blockers; `0 (Auto)` uses the calculated light range when baking. Set an explicit distance for a light that starts black or at zero intensity and has no previous nonzero range.

**Use World Space** keeps the baked projection at its original bake position and rotation. Otherwise it follows the light. Neither setting updates the recorded geometry: if a lamp moves through a room, rebake its shadows to match the new position.

## Fix Common Artifacts

| Symptom | What to try |
| --- | --- |
| A wall or prop casts no shadow | Check Layer Mask, Excluded Renderers, clip planes and that the object's material can render into the shadow camera's depth. Re-bake after changes. |
| Speckles or striped self-shadowing | Increase **Bias** a little and re-bake. Too much bias detaches the shadow from its caster. |
| Shadow floats away from an object | Reduce Bias. Also check excessive variance or blur. |
| Jagged edges | Try more **Blur** or a higher resolution. A narrower Spot angle may use the same texture more effectively. |
| Cubemap seams or uneven blur | Enable **Spherical Blur** and re-bake. It costs more during baking than planar blur. |
| Light leaks through shadowed areas | Increase Manager **Shadow Bleed Reduction** cautiously; strong values can remove faint shadow detail. |
| Moving objects leave old shadows | Use a fresh bake or the runtime baker; editor-baked shadows do not track moving casters. |
| Shadows disappear when Shading Strength is lowered | This control also reduces shadow strength; `0` disables the shadow contribution. |

**Contact Hardening** makes shadows sharper close to contact. Leave it at `0` until the basic shadow looks correct; it adds processing cost and can introduce artifacts.

### PC And Quest

Shadows use EVSM, a format that stores depth statistics so shadows can be blurred. The shared atlas uses Float precision on PC and Half precision on Android/Quest/iOS. Half precision can reveal artifacts absent from a PC preview.

The Manager has separate desktop and mobile **Shadow Min Variance** settings. Start with the mobile default of `1`; try **Shadow Bleed Reduction** around `0.2–0.4` if needed. These are starting points for comparison, not a substitute for checking the target device. Bias still needs a re-bake; Manager receiver settings can be tuned without one.

## Resolution And Memory

Per-light **Resolution** supports `16` through `2048`, or **Manager**. It controls editor and runtime shadow generation. The finished result is resized into the shared atlas, whose resolution always comes from the Manager.

For example, a light baked at `128` and a Manager atlas at `256` still occupies a `256`-pixel atlas slice. The lower setting saves bake work and source memory, but does not shrink that atlas slice. To reduce atlas memory, lower the Manager's Shadow Resolution. Doubling its resolution uses four times the memory per slice; a cubemap uses six slices.

**Quality** selects Low, Medium or High blur/contact-hardening sample counts for in-game baking. It does not change resolution. Editor baking uses its own quality preset. **Spherical Blur** applies to both editor and runtime baking.

## Bake In Game

Enable **Bake In Game** on the light; no extra component is needed. It sends one request when its runtime component first reaches `Start`. An initially inactive object sends that request on its first activation. Re-enabling it later does not bake again.

The Manager processes at most one queued light per frame. A normal Spot captures one view in that frame; a Point, Area or forced-cubemap Spot captures **all six views in the same frame**. The queue spreads lights across frames, not individual faces, so test startup and activation frame time on the target device.

The editor shadow source is removed from the build copy. After the in-game bake, the light keeps a runtime source texture as well as its place in the shared atlas. Adding another shadow source can rebuild the atlas and copy the earlier maps again. This saves downloaded shadow data, but does not eliminate runtime memory or loading work.

Keep the GameObject, Manager and bake dependencies available until the request completes. A failed request is consumed without automatic retry. Assigning a Manager after `Start` does not enqueue a new request. For explicit retries or repeated activation bakes, use the runtime baker or `BakeShadows()`.

## Runtime Shadow Baker

1. Add **Point Light Shadow Runtime Baker** and assign **Target Point Light Volume**.
2. Enable shadows on that light and configure its caster, clip, resolution and blur settings.
3. Choose **Bake On Enable** for activation-time bakes, or **Realtime** for continuous updates. Realtime takes priority.
4. For a moving light, also enable the light's **Dynamic** and Manager **Auto Update Volumes**.
5. Enter Play Mode to check it. Continuous runtime baking is not an Edit Mode Scene-view preview.

Use only one baking workflow on a light at a time; turn off its **Bake In Game** when the external baker owns startup.

Realtime mode captures a complete map on each update while the target is active. It can write directly into the Manager atlas when resolutions match. If they differ, it keeps a separate source and resizes it into the atlas. A direct result is refreshed on the next realtime update after an atlas rebuild; do not rely on it as a permanent saved shadow when the baker stops. Make a one-shot bake when you need a retained source.

Keep realtime Point and Area shadows to a small number of important lights. Reducing caster geometry, resolution and blur work is usually more useful than trying to update many six-face lights continuously.

## Scripted And External Sources

`PointLightVolumeInstance.BakeShadows()` performs a complete one-shot runtime bake. The runtime baker also exposes `BakeShadows()` for a retained one-shot result. See [UdonSharp API](./UdonSharpAPI.md#pointlightvolumeinstance) for configuration and call requirements.

If scripts enable shadows absent from the authored scene, retain the needed features in Manager **Shader Stripping** settings.

**Shadow Map** accepts a Cubemap, Texture2DArray, Render Texture or Material. External data must contain the package's EVSM moments; an ordinary black-and-white mask or a raw camera-depth texture will not work. See [Point Light Material Sources](./HowToUse_PointLightMaterialSources.md#shadow-map-materials) for the encoding and projection contract.
