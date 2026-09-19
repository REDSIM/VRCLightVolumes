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
| [AudioLink](./HowToUse_AudioLinkIntegration.md) |
| [TV Screens](./HowToUse_TVScreensIntegration.md) |
| **Debugging**<br />• [Inspect The Lighting In Scene View](#inspect-the-lighting-in-scene-view)<br />• [Inspect A Baked Volume's Grid](#inspect-a-baked-volumes-grid)<br />• [Check Runtime State In The Inspector](#check-runtime-state-in-the-inspector)<br />• [Use The Optional Avatar Debugger](#use-the-optional-avatar-debugger) |
| [How It Works](./HowToUse_HowItWorks.md) |

## Inspect The Lighting In Scene View

The Scene view's shading-mode menu, normally showing **Shaded**, contains the **Light Volumes Debug** modes. They show lighting and clustering across the scene independently of its materials.

| Mode | What it shows | Useful for |
| --- | --- | --- |
| **VRCLV SH L1** | Light Volume lighting with surface direction. | Directional lighting and transitions across surfaces. |
| **VRCLV SH L0** | The average lighting color without the directional part. | Color and brightness changes without surface direction affecting the result. |
| **VRCLV Fine Clustering** | Groups of lights in the final grid, shown as colors. | Where the grid separates unrelated Point Light Volumes. |
| **VRCLV Coarse Clustering** | Groups of lights in the larger cells. | How the coarse and fine grids differ. |

The SH views include both baked volumes and Point Light Volumes. Select **Shaded** to return to the scene's usual materials.

| Shaded: the usual materials | VRCLV SH L1: Light Volume lighting |
| --- | --- |
| ![A lantern-lit alley with textured storefronts, barrels and a red scooter](./Images/debug-shaded.jpg) | ![The same alley in VRCLV SH L1, showing lighting and shadows without material colors or textures](./Images/debug-sh-l1.jpg) |

In **Shaded**, the wood, fabric and red scooter retain their material colors and textures. **VRCLV SH L1** shows the lighting on the same geometry without those material details, making its color, direction and shadows easier to see.

Clustering colors identify groups of lights; they do not represent brightness or performance. See [Froxel Clustering](./HowToUse_FroxelClustering.md) for the Coarse and Fine views in more detail.

## Inspect A Baked Volume's Grid

Click **Preview Voxels** in a Regular Light Volume's Inspector to display its baked lighting as a grid of spheres in the Scene view. This previews only that volume's data. Other volumes and **Point Light Volumes** do not affect it.

## Check Runtime State In The Inspector

The **Light Volume**, **Point Light Volume** and **Light Volume Manager** components have a collapsible **Debug** section at the bottom of their Inspectors. It shows internal state, such as registration and active lighting data, and includes previews of textures and texture arrays where relevant. All displayed values are read-only. Some runtime values are available only in Play Mode.

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
