[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [UdonSharp API](./UdonSharpAPI.md) | [Unity Editor API](./UnityEditorAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Debugging Light Volumes

**Guides:** [Overview](./HowToUse.md) · [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) · [Point Light Volumes](./HowToUse_PointLightVolumes.md) · [Froxel Clustering](./HowToUse_FroxelClustering.md) · [Shadows](./HowToUse_Shadows.md) · [Material Sources](./HowToUse_PointLightMaterialSources.md) · [Area Light Emission](./HowToUse_AreaLightEmission.md) · [AudioLink](./HowToUse_AudioLinkIntegration.md) · [TV Screens (Older Workflow)](./HowToUse_TVScreensIntegration.md) · **Debugging** · [How It Works](./HowToUse_HowItWorks.md)

Start with a test sphere using `Light Volume Samples/Light Volume PBR`. Keep **Color** white and **Metallic** at `0`, and set **Smoothness** to `0` so reflections do not distract from the lighting. Move it between bright and dark parts of the room: its surface should change color and brightness. If it looks correct but another object does not, check that object's [shader support and settings](./CompatibleShaders.md) before changing the bake.

## Inspect The Lighting In Scene View

Open the Scene view's shading-mode menu, normally showing **Shaded**, and find **Light Volumes Debug**.

Start with **VRCLV SH L1** and move your test sphere across the problem area. If the lighting changes correctly here but looks wrong in **Shaded**, check the object's material. If the problem appears in both views, inspect the volume's bounds and baked grid below.

| Mode | What it shows | Use it to check |
| --- | --- | --- |
| **VRCLV SH L1** | Light Volume lighting evaluated with the mesh's surface normals, including L0 and L1. | Directional lighting and transitions across surfaces. |
| **VRCLV SH L0** | The average lighting color without the directional part. | Color and brightness changes without surface direction affecting the result. |
| **VRCLV Fine Clustering** | Colors identifying the possible-light sets in the final clustering grid. | Where the grid separates unrelated Point Light Volumes. |
| **VRCLV Coarse Clustering** | The possible-light sets in the larger helper cells. | How the coarse and fine grids differ. |

These modes replace the materials in Scene view. They do not show your textures, normal maps or glossy highlights, and do not prove that an object's usual shader supports Light Volumes. Return to **Shaded** to inspect the actual materials.

| Shaded: the usual materials | VRCLV SH L1: Light Volume lighting |
| --- | --- |
| ![Matte spheres receiving room lighting, with baked wall lightmaps visible behind them](./Images/debug-shaded.png) | ![The same spheres in the L1 debug view, with the outside walls black](./Images/debug-sh-l1.png) |

Compare the spheres. Shaded also shows the walls' lightmaps. The debug view samples Light Volumes instead; these walls are outside the example volume, so they appear black.

Clustering colors are identifiers, not a brightness or performance heat map. Black means an empty or unavailable light set; it does not necessarily mean broken lighting. Check **Clustering Enabled**, **Min Lights Count** and the Manager's **Debug** section. See [Froxel Clustering](./HowToUse_FroxelClustering.md) for the full workflow.

## Inspect A Baked Volume's Grid

1. Select the Light Volume and click **Preview Voxels** in its Inspector.
2. Move the Scene camera close enough to inspect the area with the problem.
3. Look for missing coverage, a grid too coarse for the shadow, or unexpected bright or dark samples.
4. Click **Preview Voxels** again to turn it off.

Before a bake, the spheres show placement. With all three baked texture fields assigned, they show that volume's stored lighting, tint and correction. This preview reads the selected volume's source textures; it does not combine every overlapping volume and Point Light.

For a gap or a sudden transition, check the bounds first, then **Weight** and **Smooth Blending**. For lost detail, adjust density in that area and rebake. See [Regular Light Volumes](./HowToUse_RegularLightVolumes.md).

## Check Runtime State In The Inspector

Enter Play Mode and expand **Debug** on the Manager or a light. These fields are read-only.

- On a Regular Light Volume, check **Manager**, **Registered** and **Active**. They show whether it belongs to a Manager and is eligible to render.
- On a Point Light Volume, check **Resolved Light Data**, **Resolved Projection** and **Resolved Shadows** for the data the shader receives.
- On the Manager, check **Cookie Array**, **Shadow Array** and **Clustering Status** when one of those features is missing.

If a Regular Light Volume's **Active** field is false, check that the GameObject and component are enabled, **Intensity** is above zero, and **Color** is not black.

Some live fields are only populated in Play Mode. Read Console errors alongside these values. If an effect works in Edit Mode but disappears in Play Mode, also check [Shader Stripping](./ForDevelopers.md#shader-feature-stripping).

## Use The Optional Avatar Debugger

The package includes a debugger for a **PC test avatar**. It reads the lighting supplied by the current world; it does not add lights to the world.

Install VRC Light Volumes in the avatar project, then choose the prefab matching your avatar tool:

| Avatar tool | Prefab, relative to `Packages/red.sim.lightvolumes/` |
| --- | --- |
| [VRCFury](https://vrcfury.com/download/) | `Extra/Light Volume Debugger/VRCFury/Light Volume Debugger VRCFury.prefab` |
| [Modular Avatar](https://modular-avatar.nadena.dev/docs/intro) | `Extra/Light Volume Debugger/ModularAvatar/Light Volume Debugger MA.prefab` |

Only the chosen avatar tool is needed. The Modular Avatar prefab declares a minimum version of **1.18.0**. Neither tool is required for the world's Light Volume setup.

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

Turn the debugger off when comparing normal appearance or measuring performance. Its overlay adds rendering work. For lighting changes suggested by these views, use [Best Practices](./BestPractices.md).
