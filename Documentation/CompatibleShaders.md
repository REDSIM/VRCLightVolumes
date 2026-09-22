[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [Scripting API](./ScriptingAPI.md) | [Shader Integration](./ForDevelopers.md) | **Compatible Shaders**

# Compatible Shaders

Install a shader that supports VRC Light Volumes and enable its Light Volumes option if it has one. Updating the world package does not update the shader on an avatar or world material.

For a quick world test, create a Material and select **Light Volume Samples > Light Volume PBR** in its Shader dropdown. It comes with the package. Assign it to a sphere to check the lighting before adjusting an avatar shader's brightness controls.

## Which Features Will I See?

| Shader integration | Result in a 3.0 world |
|---|---|
| 2.x | Baked Regular/Additive lighting and the older Point Light Volume path. Textured Area lights use an average-color fallback. |
| 3.x | Can also use Point Light Volume shadows, textured Area emission and individual specular highlights. The shader author decides which features to expose. |

Shaders with VRC Light Volumes 3.x support use [Froxel Clustering](./HowToUse_FroxelClustering.md) automatically. Older integrations still show the lighting without this optimization.

## Shaders With 3.x Support

**Version** shows when support was added. A dash means the version or date is unknown.

| Shader | Use | Version |
|---|---|---|
| [Poiyomi Toon](https://github.com/poiyomi/PoiyomiToonShader) | Toon shader for avatars | [10.0.20](https://www.poiyomi.com/changelog/2026/09/14/10-0-20) |
| [Poiyomi Pro](https://www.poiyomi.com/) | Toon shader with additional features | [10.0.12](https://www.poiyomi.com/changelog/2026/06/28/10-0-12) |
| [Mochie's Unity Shaders](https://github.com/MochiesCode/Mochies-Unity-Shaders) | PBR, toon, water, particles and other shaders | [1.74](https://github.com/MochiesCode/Mochies-Unity-Shaders/releases/tag/v1.74) |
| [VixenWear Latex Ultra](https://vixenlicous.gumroad.com/l/latex-ultra) | PBR shader for synthetic materials | — |

Check the specific material's settings: not every shader in a pack uses every lighting feature.

## Other Known Integrations

These entries record **2.x support**. Check the author's release notes for newer features and versions.

| Shader | Use | Version |
|---|---|---|
| [lilToon Shader](https://github.com/lilxyzw/lilToon) | Toon shader for avatars. | 2.0.0 |
| [UnlitWF Shaders](https://github.com/whiteflare/Unlit_WF_ShaderSuite) | Toon, fur, water and other avatar/world shaders. | 2.10.0 |
| [Filamented by Silent](https://gitlab.com/s-ilent/filamented) | PBR shader for world surfaces. | 2025-07-05 |
| [Silent Cel Shading Shader](https://gitlab.com/s-ilent/SCSS/-/tree/crosstone-testing?ref_type=heads) | Cel shading with Unity lighting support. | 2025-07-21 |
| [Silent Clear Water](https://gitlab.com/s-ilent/clear-water) | Water shader. | 2025-07-20 |
| [Silent Crispy Foliage](https://gitlab.com/s-ilent/crispy-foliage) | Foliage shader with thin detail and wind. | 2025-07-20 |
| [Unity Standard Particles Plus by Silent](https://github.com/s-ilent/unity-standard-particles-plus) | Unity particle shaders with Light Volumes support. | 2025-07-21 |
| [Graphlit Shaders and Shader Editor by z3y](https://github.com/z3y/Graphlit) | Node shader editor with Toon and PBR shaders. Use its Built-in RP integration. | 2.0.1 |
| [Unity Baked Volumetrics - Fork by Ikeiwa](https://github.com/Ikeiwa/Unity-Baked-Volumetrics) | Volumetric fog with Light Volumes support. | — |
| [Unity Shaders Plus](https://github.com/ShingenPizza/UnityShadersPlus/) | Modified versions of Unity built-in shaders. | 3 |
| [GeneLit by Momoma](https://github.com/momoma-null/GeneLit) | PBR shaders based on Filament. | 1.0.8 |
| [Cottonfox Fur Shader](https://github.com/jamestruhlar/cottonfoxfur/) | Fur shader. | — |
| [Orels Unity Shaders (Toon and PBR)](https://github.com/orels1/orels-Unity-Shaders/tree/dev) | Toon and PBR shaders; the recorded version is a prerelease. | 7.0.0 Dev 23 |
| [Moriohs Toon Shader](https://gitlab.com/xMorioh/moriohs-toon-shader) | Toon shader with PBR options. | 2.1.0 |
| [RealToon (Pro Anime/Toon Shader)](https://assetstore.unity.com/packages/vfx/shaders/realtoon-pro-anime-toon-shader-65518?aid=1100lwff7) | Anime and toon shader. | 5.0.13 |
| [Quantum Shader](https://github.com/SaphiBlue/quantumshader) | PBR shader pack made with Amplify Shader Editor. | 2025-07-24 |
| [Warren's Fast Fur Shader](https://warrenwolfy.gumroad.com/l/atntv) | Fur shader with per-pixel and per-vertex Light Volumes options. | 5.1.0 |
| [ACLS Shader](https://aciil.booth.pm/items/1779615) | Toon and realistic shading for avatars. | 2.31 |
| [The Gaze Shader](https://github.com/lunabxgg/The-Gaze-Shader) | Animated image and gaze-tracking shader. | 1.0 |
| [Xiexe's Unity Shaders](https://github.com/Xiexe/Xiexes-Unity-Shaders) | Toon and PBR shaders. | 3.7.0 |

## PC And Android

Shader support for Light Volumes and shader support for Android are separate requirements. For an Android world, use a world shader that supports both VRC Light Volumes and Android. VRChat restricts Android avatars to its [allowed mobile avatar shaders](https://creators.vrchat.com/platforms/android/quest-content-limitations/#shaders); installing this world package does not change those restrictions.

If a material receives baked lighting but has no Point Light Volume shadows or textured Area detail, check its integration version before changing the lights. If only specular highlights are missing, also check whether the shader enables that feature.

To add or correct a listing, contact **@RED_SIM** on Discord with the shader link and the first compatible version.
