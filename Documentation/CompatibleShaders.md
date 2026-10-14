[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [Scripting API](./ScriptingAPI.md) | [Shader Integration](./ForDevelopers.md) | **Compatible Shaders**

# Compatible Shaders

Install a shader that supports VRC Light Volumes and enable its Light Volumes option if it has one. Updating the world package does not update the shader on an avatar or world material.

For a quick world test, create a Material and select **Light Volume Samples > Light Volume PBR** in its Shader dropdown. It comes with the package. Assign it to a sphere to check the lighting before adjusting an avatar shader's brightness controls.

## Which Features Will I See?

| Shader integration | Result in a 3.0 world |
|---|---|
| 2.x | Baked Regular/Additive lighting and the older Point Light Volume path. Textured Area lights use an average-color fallback. |
| 3.x | Can also use Point Light Volume shadows, textured Area emission and individual specular highlights. The shader author decides which features to expose. |

<a id="shaders-that-use-the-installed-package"></a>

## Shaders With 3.x Support

| Shader | Description | Version |
|---|---|---|
| [Poiyomi Toon](https://github.com/poiyomi/PoiyomiToonShader) | A feature-rich toon shader for VRChat avatars, with multiple shading modes, flexible scene lighting and extensive material effects. Free and open source, with detailed controls for customizing the final look. | 10.0.25 |
| [Poiyomi Pro](https://www.poiyomi.com/) | The expanded edition of Poiyomi's customizable toon shader for VRChat avatars, with additional lighting, material and special-effect features for more elaborate character looks. | 10.0.20 |
| [NonToon](https://github.com/lilxyzw/NonToon) | A modular character shader combining PBR lighting with stylized shading. Supports outlines, ramp shadows, MatCaps, hair highlights and fur, with separate modules for extra features. Requires [VRCLVModule](https://github.com/lilxyzw/VRCLVModule), whose VCC dependency still targets Light Volumes 2.x. | 0.3.0 with VRCLVModule 1.2.0 |
| [Mochie's Unity Shaders](https://github.com/MochiesCode/Mochies-Unity-Shaders) | A collection of shaders including a highly customizable toon shader, a Standard shader replacement, particle shaders, water, glass and screen-space effects. | 1.77.1 |
| [VixenWear Latex Ultra](https://vixenlicous.gumroad.com/l/latex-ultra) | A PBR surface shader for latex and other glossy synthetic materials, with detailed control over highlights and reflections. Supports Light Volumes, LTCGI and AudioLink effects. | 5.4.0 |
| [MingToon](https://booth.pm/ja/items/8810209) | An advanced character toon shader with polished anime-style lighting, layered shadows, depth rim lighting and Face SDF. Includes detailed face and hair controls, PBR materials and toon specular highlights. | 0.1.13 Open Beta |
| [Orels Unity Shaders (Toon and PBR)](https://github.com/orels1/orels-Unity-Shaders/tree/dev) | A collection of practical PBR and toon shaders for Unity worlds, with a shader generator for custom materials. Enable prerelease packages in VCC to install the 7.4 version. | 7.4.0-dev.2 (prerelease) |
| [Graphlit Shaders and Shader Editor by z3y](https://github.com/z3y/Graphlit) | A node-based editor for building Toon and PBR shaders, with ready-made shaders and visual control over custom materials. Supports Built-in RP and URP. Use Built-in RP for Light Volumes. | 2.7.4 |
| [GeneLit by Momoma](https://github.com/momoma-null/GeneLit) | Physically based rendering (PBR) shaders derived from Google's [Filament](https://github.com/google/filament), offering richer material rendering than Unity's Standard shader. Includes dedicated standard, cloth and subsurface shading models. | 1.0.9 |
| [Cottonfox Fur Shader](https://github.com/jamestruhlar/cottonfoxfur/) | A fur shader with configurable fur length, density, color and highlights. Includes AudioLink effects and receives Light Volumes lighting. | Current source |
| [Unity Baked Volumetrics - Fork by Ikeiwa](https://github.com/Ikeiwa/Unity-Baked-Volumetrics) | A volumetric fog shader that receives Light Volumes lighting, adding visible light and atmosphere to a scene. A fork of [Unity Baked Volumetrics](https://github.com/frostbone25/Unity-Baked-Volumetrics). | Current source |
| [Moriohs Toon Shader](https://codeberg.org/Morioh/moriohs-toon-shader) | A customizable toon shader that combines stylized NPR shading with PBR material features. Designed to respond to Unity's scene lighting while giving detailed control over the final look. | 2.1.2 (source) |
| [BigiShader](https://github.com/bigibas123/BigiShader) | A collection of customizable avatar and world shaders with toon-style rendering, specular materials and AudioLink effects. Includes shaders for video screens, particles, billboards and other visual effects. | 4.0.6 |
| [dhk Shaders](https://github.com/you5248/dhk-shaders) | A Standard-compatible world shader with bicubic lightmaps, MonoSH and packed material maps. Adds detail textures, rim lighting and optional Light Volumes specular, and includes a Quest version. | 1.5.0 |

Check the specific material's settings: not every shader in a pack uses every lighting feature.

<a id="other-known-integrations"></a>

## Shaders With 2.x Support

| Shader | Description | Version |
|---|---|---|
| [lilToon Shader](https://github.com/lilxyzw/lilToon) | A Japanese toon shader for VRChat avatars, with anime-style shading and detailed controls for shadows, outlines, rim lighting and material appearance. | 2.0.0 |
| [UnlitWF Shaders](https://github.com/whiteflare/Unlit_WF_ShaderSuite) | A general-purpose shader pack for VRChat avatars and worlds, including toon, fur, water and other shader variants. | 2.10.0 |
| [Filamented by Silent](https://gitlab.com/s-ilent/filamented) | A PBR replacement for Unity's Standard shader, with Mono SH, Specular AA and Specular Occlusion. A good choice for detailed, realistic world materials. | 2025-07-05 |
| [Silent Cel Shading Shader](https://gitlab.com/s-ilent/SCSS/-/tree/crosstone-testing?ref_type=heads) | A feature-rich cel-shading shader designed to work with Unity's scene lighting while keeping an anime-style appearance. Includes outlines and detailed controls over shadows and materials. | 2025-07-21 |
| [Silent Clear Water](https://gitlab.com/s-ilent/clear-water) | A water shader for Unity and VRChat that uses reflections, refraction and animated waves to create clean-looking water surfaces. | 2025-07-20 |
| [Silent Crispy Foliage](https://gitlab.com/s-ilent/crispy-foliage) | A shader for semitransparent plants and foliage, including thin leaves and other fine details. Supports wind animation to bring vegetation to life. | 2025-07-20 |
| [Unity Standard Particles Plus by Silent](https://github.com/s-ilent/unity-standard-particles-plus) | Unity's Standard Particle shaders, extended with Light Volumes support. | 2025-07-21 |
| [Unity Shaders Plus](https://github.com/ShingenPizza/UnityShadersPlus/) | Modified versions of Unity's built-in Standard, particle and unlit shaders, with extra features while retaining compatibility with existing materials. Includes visibility controls for VRChat cameras and mirrors. | 3 |
| [RealToon (Pro Anime/Toon Shader)](https://assetstore.unity.com/packages/vfx/shaders/realtoon-pro-anime-toon-shader-65518?aid=1100lwff7) | An advanced anime and toon shader for characters and objects, with controls for a range of cartoon and stylized looks. Suitable for games, animation, film and illustrations. | 5.0.13 |
| [Quantum Shader](https://github.com/SaphiBlue/quantumshader) | A shader pack made with Amplify Shader Editor (ASE), combining Unity-style PBR materials with custom AudioLink effects. Includes Light Volumes support across the pack. | 2025-07-24 |
| [Warren's Fast Fur Shader](https://warrenwolfy.gumroad.com/l/atntv) | A fur shader optimized for speed, with per-pixel and per-vertex Light Volumes options and anisotropic hair highlights. | 5.1.0 |
| [ACLS Shader](https://aciil.booth.pm/items/1779615) | A highly configurable avatar shader for toon (NPR) and realistic (PBR) styles, designed to adapt to the varied lighting of VRChat worlds. Gives detailed control over how materials respond to the scene. | 2.31 |
| [The Gaze Shader](https://github.com/lunabxgg/The-Gaze-Shader) | A 2D animation and gaze-tracking shader for Unity and VRChat. Plays GIFs or sprite sheets in 3D scenes and follows the viewer's perspective, creating an interactive feeling of being watched. | 1.0 |
| [Xiexe's Unity Shaders](https://github.com/Xiexe/Xiexes-Unity-Shaders) | A shader collection that balances stylized character looks with the surrounding scene lighting. Supports both NPR and PBR workflows, with extensive controls for tuning each material. | 3.7.0 |
| [Clearwater VRC](https://github.com/bmbb93/clearwater-vrc) | A water and beach shader package with waves, foam, underwater rendering and a time-of-day sky. Includes tools for building shorelines and matching the water, beach and sky lighting. | 1.2.0 |

## PC And Android

Shader support for Light Volumes and shader support for Android are separate requirements. For an Android world, use a world shader that supports both VRC Light Volumes and Android. VRChat restricts Android avatars to its [allowed mobile avatar shaders](https://creators.vrchat.com/platforms/android/quest-content-limitations/#shaders). Installing this world package does not change those restrictions.

If a material receives baked lighting but has no Point Light Volume shadows or textured Area detail, check its integration version before changing the lights. If only specular highlights are missing, also check whether the shader enables that feature.

To add or correct a listing, contact **@RED_SIM** on Discord with the shader link and the first compatible version.
