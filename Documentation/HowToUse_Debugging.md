[VRC Light Volumes](../README.md) | **How to Use** | [Best Practices](./BestPractices.md) | [Scripting API](./ScriptingAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

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
| **Debugging**<br />• [Inspect The Lighting In Scene View](#inspect-the-lighting-in-scene-view)<br />• [Inspect A Baked Volume's Grid](#inspect-a-baked-volumes-grid)<br />• [Check Runtime State In The Inspector](#check-runtime-state-in-the-inspector)<br />• [Avatar Debugger](#avatar-debugger) |
| [How It Works](./HowToUse_HowItWorks.md) |

## Inspect The Lighting In Scene View

The Scene view's shading-mode menu, normally showing **Shaded**, contains the **Light Volumes Debug** modes. They show lighting, overlaps and clustering across the scene independently of its materials.

| Mode | Description |
| --- | --- |
| **VRCLV&nbsp;SH&nbsp;L1** | Directional Light Volume lighting, for inspecting how light falls across surfaces. |
| **VRCLV&nbsp;SH&nbsp;L0** | Lighting color and brightness without directional shading. |
| **VRCLV&nbsp;Overdraw** | Brighter colors show more overlapping Point Light Volumes, Additive Light Volumes and Regular Light Volumes. Where two Regular Light Volumes blend, both are counted. |
| **VRCLV&nbsp;Fine&nbsp;Clustering** | Groups of Point Light Volumes in the final clustering grid, shown as colors. |
| **VRCLV&nbsp;Coarse&nbsp;Clustering** | Light groups in the larger coarse cells, shown as colors for comparison with the Fine view. |

The SH views include both baked volumes and Point Light Volumes. Select **Shaded** to return to the scene's usual materials.

<table width="100%">
  <thead>
    <tr>
      <th width="50%">Shaded</th>
      <th width="50%">VRCLV&nbsp;SH&nbsp;L1</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td width="50%"><a href="./Images/debug-shaded.jpg"><img src="./Images/debug-shaded.jpg" alt="A lantern-lit alley with textured storefronts, barrels and a red scooter" width="100%"></a></td>
      <td width="50%"><a href="./Images/debug-sh-l1.jpg"><img src="./Images/debug-sh-l1.jpg" alt="The same alley in VRCLV SH L1, showing lighting and shadows without material colors or textures" width="100%"></a></td>
    </tr>
  </tbody>
</table>

**VRCLV SH L1** applies Light Volume lighting to all surfaces, including those that normally use lightmaps. This lets you inspect baked volume lighting throughout the scene, even though the result can differ from the scene's usual appearance.

<table width="100%">
  <thead>
    <tr>
      <th width="50%">Shaded</th>
      <th width="50%">VRCLV&nbsp;Overdraw</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td width="50%"><a href="./Images/debug-overdraw-shaded.jpg"><img src="./Images/debug-overdraw-shaded.jpg" alt="A lantern-lit alley with colored light beams across the ground" width="100%"></a></td>
      <td width="50%"><a href="./Images/debug-overdraw.jpg"><img src="./Images/debug-overdraw.jpg" alt="The same alley in VRCLV Overdraw, with brighter orange where more lights and volumes overlap" width="100%"></a></td>
    </tr>
  </tbody>
</table>

**VRCLV Overdraw** highlights areas with many overlapping lights and volumes. Brighter colors mean more overlaps from Point Light Volumes, Additive Light Volumes and Regular Light Volumes, including both Regular volumes where they blend.

<table width="100%">
  <thead>
    <tr>
      <th width="50%">Shaded</th>
      <th width="50%">VRCLV&nbsp;Fine&nbsp;Clustering</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td width="50%"><a href="./Images/debug-clustering-shaded.jpg"><img src="./Images/debug-clustering-shaded.jpg" alt="The alley in Shaded view" width="100%"></a></td>
      <td width="50%"><a href="./Images/debug-clustering-fine.jpg"><img src="./Images/debug-clustering-fine.jpg" alt="The same view with colored Fine Clustering regions" width="100%"></a></td>
    </tr>
  </tbody>
</table>

Clustering colors identify groups of lights. They do not represent brightness or performance. See [Froxel Clustering](./HowToUse_FroxelClustering.md) for the Coarse and Fine views in more detail.

## Inspect A Baked Volume's Grid

Click **Preview Voxels** in a Regular Light Volume's Inspector to display its baked lighting as a grid of spheres in the Scene view. This previews only that volume's data. Other volumes and **Point Light Volumes** do not affect it.

## Check Runtime State In The Inspector

The **Light Volume**, **Point Light Volume** and **Light Volume Manager** components have a collapsible **Debug** section at the bottom of their Inspectors. It shows internal state, such as registration and active lighting data, and includes previews of textures and texture arrays where relevant. All displayed values are read-only. Some runtime values are available only in Play Mode.

## Avatar Debugger

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
