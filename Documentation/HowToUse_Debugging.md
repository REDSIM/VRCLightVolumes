[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [Scripting API](./ScriptingAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Debugging Light Volumes

| Menu |
| --- |
| [Overview](./HowToUse.md) |
| [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) |
| [Point Light Volumes](./HowToUse_PointLightVolumes.md) |
| [Froxel Clustering](./HowToUse_FroxelClustering.md) |
| [Shadows](./HowToUse_Shadows.md) |
| [Material Sources](./HowToUse_PointLightMaterialSources.md) |
| [Area Light Emission](./HowToUse_AreaLightEmission.md) |
| [AudioLink](./HowToUse_AudioLinkIntegration.md) |
| [TV Screens (Older Workflow)](./HowToUse_TVScreensIntegration.md) |
| **Debugging**<br />• [Inspect The Lighting In Scene View](#inspect-the-lighting-in-scene-view)<br />• [Inspect A Baked Volume's Grid](#inspect-a-baked-volumes-grid)<br />• [Check Runtime State In The Inspector](#check-runtime-state-in-the-inspector)<br />• [Use The Optional Avatar Debugger](#use-the-optional-avatar-debugger) |
| [How It Works](./HowToUse_HowItWorks.md) |

Start with a test sphere using `Light Volume Samples/Light Volume PBR`. Keep **Color** white and **Metallic** at `0`, and set **Smoothness** to `0` so reflections do not distract from the lighting. Move it between bright and dark parts of the room: its surface should change color and brightness. If it looks correct but another object does not, check that object's [shader support and settings](./CompatibleShaders.md) before changing the bake.

## Inspect The Lighting In Scene View

Open the Scene view's shading-mode menu, normally showing **Shaded**, and find **Light Volumes Debug**.

Start with **VRCLV SH L1** and move your test sphere across the problem area. If the lighting changes correctly here but looks wrong in **Shaded**, check the object's material. If the problem appears in both views, inspect the volume's bounds and baked grid below.

| Mode | What it shows | Use it to check |
| --- | --- | --- |
| **VRCLV SH L1** | Light Volume lighting with surface direction. | Directional lighting and transitions across surfaces. |
| **VRCLV SH L0** | The average lighting color without the directional part. | Color and brightness changes without surface direction affecting the result. |
| **VRCLV Fine Clustering** | Groups of lights in the final grid, shown as colors. | Where the grid separates unrelated Point Light Volumes. |
| **VRCLV Coarse Clustering** | Groups of lights in the larger cells. | How the coarse and fine grids differ. |

These views replace the materials. Return to **Shaded** to check the object's own textures, shading and Light Volume support.

| Shaded: the usual materials | VRCLV SH L1: Light Volume lighting |
| --- | --- |
| ![Matte spheres receiving room lighting, with baked wall lightmaps visible behind them](./Images/debug-shaded.png) | ![The same spheres in the L1 debug view, with the outside walls black](./Images/debug-sh-l1.png) |

Compare the spheres. The walls have lightmaps in **Shaded**, but appear black in the debug view because they are outside this Light Volume.

Clustering colors are identifiers, not a brightness or performance heat map. Black means an empty or unavailable light set; it does not necessarily mean broken lighting. Check **Clustering Enabled**, **Min Lights Count** and the Manager's **Debug** section. See [Froxel Clustering](./HowToUse_FroxelClustering.md) for the full workflow.

## Inspect A Baked Volume's Grid

1. Select the Light Volume and click **Preview Voxels** in its Inspector.
2. Move the Scene camera close enough to inspect the area with the problem.
3. Look for missing coverage, a grid too coarse for the shadow, or unexpected bright or dark samples.
4. Click **Preview Voxels** again to turn it off.

Before a bake, the spheres show placement. After baking, they show the selected volume's lighting, tint and correction. Other volumes and Point Lights do not affect this preview.

For a gap or a sudden transition, check the bounds first, then **Weight** and **Smooth Blending**. For lost detail, adjust density in that area and rebake. See [Regular Light Volumes](./HowToUse_RegularLightVolumes.md).

## Check Runtime State In The Inspector

Enter Play Mode and expand **Debug** on the Manager or a light. These fields are read-only.

- On a Regular Light Volume, check **Manager**, **Registered** and **Active** to see whether it can contribute lighting.
- On a Point Light Volume, check **Resolved Light Data**, **Resolved Projection** and **Resolved Shadows** for the data the shader receives.
- On the Manager, check **Cookie Array**, **Shadow Array** and **Clustering Status** when one of those features is missing.

If a Regular Light Volume's **Active** field is false, check that the GameObject and component are enabled, **Intensity** is above zero, and **Color** is not black.

Some live fields are only populated in Play Mode. Read Console errors alongside these values. If an effect works in Edit Mode but disappears in Play Mode, also check [Shader Stripping](./ForDevelopers.md#shader-feature-stripping).

## Use The Optional Avatar Debugger

Use the optional **PC avatar debugger** to inspect the current world's lighting in VRChat.

Install VRC Light Volumes in the avatar project, then choose the prefab matching your avatar tool:

| Avatar tool | Prefab, relative to `Packages/red.sim.lightvolumes/` |
| --- | --- |
| [VRCFury](https://vrcfury.com/download/) | `Extra/Light Volume Debugger/VRCFury/Light Volume Debugger VRCFury.prefab` |
| [Modular Avatar](https://modular-avatar.nadena.dev/docs/intro) | `Extra/Light Volume Debugger/ModularAvatar/Light Volume Debugger MA.prefab` |

Install the tool for your chosen prefab. The Modular Avatar version requires **1.18.0** or newer.

1. Drag one prefab under the avatar's root object.
2. Build the avatar using its normal VRChat Avatar SDK workflow.
3. Wear it in a world using Light Volumes.
4. Open the avatar's **LV Debugger** menu and enable **Toggle**.

The prefab may appear empty in the Editor: its activation animation assigns the display mesh. Use its menu toggle rather than filling in the empty Mesh Filter manually.

### Debugger Controls

| Control | What to look for |
| --- | --- |
| **Selected Volume** | The selected Regular or Additive volume's baked lighting samples. These exclude separate Point Light contributions. |
| **All Volume Bounds** | Boxes around active volumes. Default colors are cyan for Regular and orange for Additive. |
| **All Lights** | Point/Spot/Area light icons and Area emitter rectangles. For effective light ranges, use **Debug Range** on the world light in Unity. |
| **Auto Volume ID** | Automatically selects a volume containing the camera, preferring Regular volumes. If none contains it, the manual ID is used. |
| **Volume ID** | Select a runtime volume slot manually with **Auto Volume ID** off. Slots can change as volumes are enabled or reordered. |
| **Sphere Size / Draw Amount** | Make the sample display smaller or less crowded. Both must be above zero to see samples. |
| **Local Only** (Modular Avatar) | Show the debugger only to its wearer. Enabled by default. The VRCFury prefab does not include this menu control. |

Modular Avatar places the three display modes under **Draw Mode**. VRCFury uses a **Mode** slider for the same displays. **Auto Volume ID** starts enabled in both prefabs.

Turn the debugger off when comparing normal appearance or measuring frame rate. See [Best Practices](./BestPractices.md) for lighting adjustments.
