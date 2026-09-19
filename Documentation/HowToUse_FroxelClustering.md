[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [Scripting API](./ScriptingAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Froxel Clustering

| Menu |
| --- |
| [Overview](./HowToUse.md) |
| [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) |
| [Point Light Volumes](./HowToUse_PointLightVolumes.md) |
| **Froxel Clustering**<br />• [Setup Froxel Clustering](#setup-froxel-clustering)<br />• [How Froxel Clustering Works](#how-froxel-clustering-works)<br />• [Debug Views](#debug-views)<br />• [Shadow Culling (Hi-Z)](#shadow-culling-hi-z) |
| [Shadows](./HowToUse_Shadows.md) |
| [Material Sources](./HowToUse_PointLightMaterialSources.md) |
| [AudioLink](./HowToUse_AudioLinkIntegration.md) |
| [TV Screens](./HowToUse_TVScreensIntegration.md) |
| [Debugging](./HowToUse_Debugging.md) |
| [How It Works](./HowToUse_HowItWorks.md) |

A **froxel** is a small 3D cell in the camera's viewing volume, called the **frustum**. Froxel Clustering divides this volume into a grid and records which Point, Spot and Area lights can affect each cell.

Clustering works best with many lights, especially dozens spread across a scene with little overlap. If many large lights cover the same area, clustering cannot solve that overlap: those lights still need to be calculated there.

Clustering usually improves performance, and the lighting will look the same. It still takes GPU time to build the grid, so overly high **Angular Density** or **Slices Count**, or heavy light overlap, can outweigh the savings. Compare frame time with clustering on and off for each setup on the target device.

## Setup Froxel Clustering

Enable **Clustering Enabled** in the **Light Volume Manager's Froxel Clustering** section. Its settings control when clustering runs and how detailed the grid is:

| Setting | What it does |
| --- | --- |
| **Clustering Enabled** | Enables Froxel Clustering. |
| **Min Lights Count** | Minimum active Point Light Volumes needed for clustering to run. |
| **Angular Density** | Grid resolution across the view. Higher values separate nearby lights more precisely, but take more time to process. |
| **Slices Count** | Number of depth slices between the camera's near and far clipping planes. More slices separate lights at different distances more precisely. |
| **Coarse Reduction** | Divides the final grid resolution along all three axes to build the Coarse grid. |
| [**Shadow&nbsp;Culling**&nbsp;(Hi&#8209;Z)](#shadow-culling-hi-z) | Skips lights in cells fully covered by their shadows. |

**Coarse Reduction** of **2x** means about **8 times fewer froxels** in the Coarse grid; **4x** means **64 times fewer**, and **8x** means **512 times fewer**. A larger reduction makes the Coarse pass cheaper, but can leave more lights for the Final pass to check.

The Manager's **Debug** foldout shows **Clustering Status** and **Active Point Lights**, so you can check whether clustering is active.

## How Froxel Clustering Works

Instead of checking every light at every shaded pixel, clustering prepares a list of possible lights for each froxel:

1. **Coarse:** checks light ranges and shapes against a grid of large cells. Each cell keeps only the lights that could reach it.
2. **Final (Fine):** checks the smaller cells inside each Coarse cell, using only the lights kept by the Coarse pass.
3. **Surface shading:** finds the pixel's froxel and calculates lighting only from its final list.

The two passes avoid checking every light against every cell in the full-resolution grid. A finer grid can exclude more lights, but also takes more work to build.

Shaders supporting **VRC Light Volumes 3.x or later** use clustering automatically through the standard lighting functions. No separate shader integration is needed.

> [!NOTE]
> Mirrors, VRChat cameras and other cameras do not get a separate clustering grid. They can reuse the player's grid only for parts of the world inside the player's frustum. Outside it, lights remain visible and correct, but use the normal light list without clustering.

## Debug Views

To view the clustering grids, use **Light Volumes Debug** in the Scene view's draw-mode menu:

- **VRCLV Coarse Clustering:** light groups in the Coarse grid.
- **VRCLV Fine Clustering:** light groups in the Final grid.

Colors identify groups of possible lights, not brightness or rendering cost.

The same view with 12 Point Light Volumes:

| Shaded | Coarse | Fine |
| --- | --- | --- |
| ![Twelve lights in the normal Shaded view.](./Images/clustering-shaded.png) | ![Coarse clustering: large cells group possible lights.](./Images/clustering-coarse.png) | ![Fine clustering: smaller cells narrow down the light groups.](./Images/clustering-fine.png) |

## Shadow Culling (Hi-Z)

Enable **Shadow Culling** when shadows cover large parts of a light's range. Hi-Z stands for **Hierarchical Z Buffer**. It uses several levels of shadow-map depth data to find fully shadowed cells. Clustering can then skip that light in those cells, saving lighting calculations. Compare frame time with it on and off to check the benefit.

> [!IMPORTANT]
> A light's **Shading Strength** must be **1**; otherwise, Shadow Culling cannot skip that light. The runtime baker's **Realtime** mode also excludes its light from Hi-Z.
