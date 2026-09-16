[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [UdonSharp API](./UdonSharpAPI.md) | [Unity Editor API](./UnityEditorAPI.md) | [Shader Integration](./ForDevelopers.md) | **Compatible Shaders**

# Compatible Shaders

Install a shader that supports Light Volumes and enable its Light Volumes option if it has one. Updating the world package does not update the shader on an avatar or world material.

For a quick world test, create a Material and select **Light Volume Samples > Light Volume PBR** in its Shader dropdown. This shader comes with the package; importing Samples is not required. Assign it to a sphere to check your lighting before adjusting an avatar shader's own brightness or shading controls.

## Which Features Will I See?

| Shader integration | Result in a 3.0 world |
|---|---|
| 2.x | Baked Regular/Additive lighting and the older Point Light Volume path. Textured Area lights use an average-color fallback. |
| 3.x | Can also use Point Light Volume shadows, textured Area emission and individual specular highlights. The shader author decides which features to expose. |

Froxel Clustering needs an integration using the current clustering-capable include and a supported shader target. A shader using an earlier include continues through the ordinary light loop. A 3.x label alone does not guarantee every later addition.

## Shaders With 3.x Support

| Shader | Use | Version / source |
|---|---|---|
| [Poiyomi Toon](https://github.com/poiyomi/PoiyomiToonShader) | Toon shader for avatars | 10.0.20 adds 3.0 lighting and specular support; see the [official changelog](https://www.poiyomi.com/changelog). |
| [Poiyomi Pro](https://www.poiyomi.com/) | Toon shader with additional features | 10.0.12 adds 3.0 support; see the [official changelog](https://www.poiyomi.com/changelog/2026/06/28/10-0-12). |
| [Mochie's Unity Shaders](https://github.com/MochiesCode/Mochies-Unity-Shaders) | PBR, toon, water, particles and other shaders | 1.74 introduced the 3.0 beta integration; 1.74.1 fixed it and added the new specular path. Use a newer release for subsequent fixes; see [release notes](https://github.com/MochiesCode/Mochies-Unity-Shaders/releases). |
| [VixenWear Latex Ultra](https://vixenlicous.gumroad.com/l/latex-ultra) | PBR shader for synthetic materials | Listed with 3.x support; the first compatible version has not been recorded here. |

Shader author release notes were checked on 16 September 2026 for Poiyomi and Mochie. Check the specific material's settings: not every shader in a pack uses every lighting feature.

## Other Known Integrations

These are the versions or dates recorded when 2.x support was added to this list. They are **not minimum versions for every 3.x feature**, and newer releases may have added further support. Follow each author's release notes for the version you install.

| Shader | Use | Recorded 2.x-compatible version / date |
|---|---|---|
| [lilToon Shader](https://github.com/lilxyzw/lilToon) | Toon shader for avatars. | v.2.0.0 |
| [UnlitWF Shaders](https://github.com/whiteflare/Unlit_WF_ShaderSuite) | Toon, fur, water and other avatar/world shaders. | 2025/08/03 (2.10.0) |
| [Filamented by Silent](https://gitlab.com/s-ilent/filamented) | PBR shader for world surfaces. | Jul 05, 2025 |
| [Silent Cel Shading Shader](https://gitlab.com/s-ilent/SCSS/-/tree/crosstone-testing?ref_type=heads) | Cel shading with Unity lighting support. | Jul 21, 2025 |
| [Silent Clear Water](https://gitlab.com/s-ilent/clear-water) | Water shader. | Jul 20, 2025 |
| [Silent Crispy Foliage](https://gitlab.com/s-ilent/crispy-foliage) | Foliage shader with thin detail and wind. | Jul 20, 2025 |
| [Unity Standard Particles Plus by Silent](https://github.com/s-ilent/unity-standard-particles-plus) | Unity particle shaders with Light Volumes support. | Jul 21, 2025 |
| [Graphlit Shaders and Shader Editor by z3y](https://github.com/z3y/Graphlit) | Node shader editor with Toon and PBR shaders. Use its Built-in RP integration. | v.2.0.1 |
| [Unity Baked Volumetrics - Fork by Ikeiwa](https://github.com/Ikeiwa/Unity-Baked-Volumetrics) | Volumetric fog with Light Volumes support. | - |
| [Unity Shaders Plus](https://github.com/ShingenPizza/UnityShadersPlus/) | Modified versions of Unity built-in shaders. | v3 |
| [GeneLit by Momoma](https://github.com/momoma-null/GeneLit) | PBR shaders based on Filament. | v.1.0.8 |
| [Cottonfox Fur Shader](https://github.com/jamestruhlar/cottonfoxfur/) | Fur shader. | - |
| [Orels Unity Shaders (Toon and PBR)](https://github.com/orels1/orels-Unity-Shaders/tree/dev) | Toon and PBR shaders; the recorded version is a prerelease. | v7.0.0 Dev 23 |
| [Moriohs Toon Shader](https://gitlab.com/xMorioh/moriohs-toon-shader) | Toon shader with PBR options. | v.2.1.0 |
| [RealToon (Pro Anime/Toon Shader)](https://assetstore.unity.com/packages/vfx/shaders/realtoon-pro-anime-toon-shader-65518?aid=1100lwff7) | Anime and toon shader. | v.5.0.13 |
| [Quantum Shader](https://github.com/SaphiBlue/quantumshader) | PBR shader pack made with Amplify Shader Editor. | Jul 24, 2025 |
| [Warren's Fast Fur Shader](https://warrenwolfy.gumroad.com/l/atntv) | Fur shader with per-pixel and per-vertex Light Volumes options. | v5.1.0 |
| [ACLS Shader](https://aciil.booth.pm/items/1779615) | Toon and realistic shading for avatars. | v.2.31 |
| [The Gaze Shader](https://github.com/lunabxgg/The-Gaze-Shader) | Animated image and gaze-tracking shader. | v1.0 |
| [Xiexe's Unity Shaders](https://github.com/Xiexe/Xiexes-Unity-Shaders) | Toon and PBR shaders. | v3.7.0 |

## PC And Android

Shader support for Light Volumes and shader support for Android are separate requirements. For an Android world, use a compatible world shader that also supports that platform. VRChat restricts Android avatars to its [allowed mobile avatar shaders](https://creators.vrchat.com/platforms/android/quest-content-limitations/#shaders); installing this world package does not change those restrictions.

If a material receives baked lighting but has no Point Light Volume shadows or textured Area detail, check its integration version before changing the lights. If only specular highlights are missing, also check whether the shader enables that feature.

To add or correct a listing, contact **@RED_SIM** on Discord with the shader link and the first compatible version.
