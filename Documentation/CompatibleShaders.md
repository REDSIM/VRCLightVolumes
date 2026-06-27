[VRC Light Volumes](../README.md) | [How to Use](../Documentation/HowToUse.md) | [Best Practices](../Documentation/BestPractices.md) | [Udon Sharp API](../Documentation/UdonSharpAPI.md) | [For Shader Developers](../Documentation/ForShaderDevelopers.md) | **Compatible Shaders**

# Compatible Shaders

## Light Volumes v.3.0.0+ compatible
*Shaders yet to be added. Stay tuned!*

VRC Light Volumes v.3.0.0+ shader integration is required for Point Light Volume shadows, per-surface Point Light Volume shading, full textured **Area Light Emission**, and the new individual Point Light Volume specular path. Older v.2.x compatible shaders still receive the average-color fallback for Area Light cookies, but they will not show the projected texture detail or size-aware individual specular highlights.

## Light Volumes v.2.0.0+ compatible
All of these shaders also work with v.3.0.0, but without Point Light Volume shadows, per-surface Point Light Volume shading and size-aware individual Point Light Volume specular support.

| Shader                                                       | Description                                                  | Supported since |
| ------------------------------------------------------------ | ------------------------------------------------------------ | :-------------: |
| [Poiyomi Toon Shader](https://github.com/poiyomi/PoiyomiToonShader) | One of the most popular toon shaders in VRChat. **Poiyomi Shaders** are feature-rich shaders for Unity's Built-In Rendering Pipeline, intended for use with **VRChat.** They support multiple shading modes and robust light handling, and are designed to be easy-to-use and performant. They're also **free** and **open-source**! |     v.9.2.67     |
| [lilToon Shader](https://github.com/lilxyzw/lilToon)         | The most popular Japanese toon shader in VRChat. |    v.2.0.0     |
| [UnlitWF Shaders](https://github.com/whiteflare/Unlit_WF_ShaderSuite) | General purpose shader pack for VRC Avatars/Worlds, including Toon Shader, Fur Shader, Water Shader, etc... | 2025/08/03 (2.10.0) |
| [Filamented by Silent](https://gitlab.com/s-ilent/filamented) | One of the best PBR shaders available. Supports Mono SH, Specular AA, Specular Occlusion and more. Highly recommended to use instead of Standard Unity shader. |  Jul 05, 2025   |
| [Silent Cel Shading Shader](https://gitlab.com/s-ilent/SCSS/-/tree/crosstone-testing?ref_type=heads) | Simple, but powerful toon shader. Shaders for Unity for cel shading, designed to take Unity's lighting into account while also presenting materials in the best possible way. Featuring lots of features with good performance! |  Jul 21, 2025   |
| [Silent Clear Water](https://gitlab.com/s-ilent/clear-water) | A simple water shader for Unity/VRchat that (ab)uses some Unity features to get nice, clean looking water. |  Jul 20, 2025   |
| [Silent Crispy Foliage](https://gitlab.com/s-ilent/crispy-foliage) | A shader for Unity/VRchat that's designed for rendering semitransparent plants and plant-like objects with thin details that blow in the wind. |  Jul 20, 2025   |
| [Unity Standard Particles Plus by Silent](https://github.com/s-ilent/unity-standard-particles-plus) | Unity Particle shaders, but with Light Volumes support.      |  Jul 21, 2025   |
| [Graphlit Shaders and Shader Editor by z3y](https://github.com/z3y/Graphlit) | A node shader editor for Toon and PBR shaders. Supports the Built-In and Universal Render Pipeline. |     v.2.0.1     |
| [Mochies Unity Shaders](https://github.com/MochiesCode/Mochies-Unity-Shaders) | A collection of shaders including a highly customizable toon shader, a standard shader replacement, particle shader, water shader, glass shader, and screenspace effect shader. |     v1.62.3     |
| [Unity Baked Volumetrics - Fork by Ikeiwa](https://github.com/Ikeiwa/Unity-Baked-Volumetrics) | Volumetric Fog shader that supports Light Volumes. A for of original [Unity Baked Volumetrics](https://github.com/frostbone25/Unity-Baked-Volumetrics) |        -        |
| [Unity Shaders Plus](https://github.com/ShingenPizza/UnityShadersPlus/) | Unity's built-in shaders, some of them edited to add some minor features, while keeping compatibility with those standard shaders that people are commonly using already. |       v3        |
| [GeneLit by Momoma](https://github.com/momoma-null/GeneLit)  | Physically based rendering (PBR) shaders for Unity derived from [Filament shaders](https://github.com/google/filament). More beautiful and accurate rendering than Unity Standard shaders. |     v.1.0.8     |
| [Cottonfox Fur Shader](https://github.com/jamestruhlar/cottonfoxfur/) | A fur shader that supports Light Volumes.                     |        -        |
| [Orels Unity Shaders (Toon and PBR)](https://github.com/orels1/orels-Unity-Shaders/tree/dev) | Really good and performant PBR Shaders. (To access it you will need to enable "Show Pre-Release Packages" in VCC Settings) |     v7.0.0 Dev 23     |
| [Moriohs Toon Shader](https://gitlab.com/xMorioh/moriohs-toon-shader) | Toon/Master Shader that not just correctly respects the entirety of Unity's Lighting System in a NPR Workflow but would also include several PBR approaches for different features. |     v.2.1.0     |
| [RealToon (Pro Anime/Toon Shader)](https://assetstore.unity.com/packages/vfx/shaders/realtoon-pro-anime-toon-shader-65518?aid=1100lwff7) | An Pro/Advance Anime/Toon Shader to make your characters & objects to look as close to real Anime/Cartoon. Other Stylized look are also possible. (For Games, Film/Animations & Illustrations/Arts) |    v.5.0.13     |
| [Quantum Shader](https://github.com/SaphiBlue/quantumshader) | A shader pack made with ASE (Amplify Shader Editor) with Unity like PBR options and custom audiolink. | Jul 24, 2025 |
| [Warren's Fast Fur Shader](https://warrenwolfy.gumroad.com/l/atntv) | A heavily speed-optimized fur shader. Supports both per-pixel and per-vertex Light Volumes, with anisotropic highlighting for the hairs. |        v5.1.0        |
| [ACLS Shader](https://aciil.booth.pm/items/1779615)          | Designed to match or fallback against the randomness of VRCHAT user generated maps. Its aware how world lighting works and what most typical problems are. Nearly all aspects of lighting context and using it as materials can be controlled for Toony or Realistic styles (NPR/PBR). |    v.2.31     |
| [The Gaze Shader](https://github.com/lunabxgg/The-Gaze-Shader)          | A 2D animation rendering and gaze-tracking system tailored for Unity and VRChat creators. It not only enables your GIFs or Sprite Sheets to play vividly in 3D space but also automatically tracks the player's perspective, creating an interactive sensation of "being watched." |    v1.0     |
| [Xiexe's Unity Shaders](https://github.com/Xiexe/Xiexes-Unity-Shaders)          | Xiexe's Unity Shaders is a set of shaders for Unity that aim to strike a mix between user desired looks and respecting the environment's lighting. It supports both NPR and PBR workflows, as well as a host of features for tweaking each. |    v3.7.0     |


*More shaders yet to be added. Stay tuned!*

Contact me in Discord if you want your shader to be added in this list.
Discord: @RED_SIM
