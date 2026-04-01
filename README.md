**VRC Light Volumes** | [How to Use](./Documentation/HowToUse.md) | [Best Practices](./Documentation/BestPractices.md) | [Udon Sharp API](./Documentation/UdonSharpAPI.md) | [For Shader Developers](./Documentation/ForShaderDevelopers.md) | [Compatible Shaders](./Documentation/CompatibleShaders.md)

<p align="center"> <img src="./Documentation/LogoMain.png#gh-dark-mode-only" alt="VRC Light Volumes Main Logo" width="627" /></p>
<p align="center"> <img src="./Documentation/LogoMainBright.png#gh-light-mode-only" alt="VRC Light Volumes Main Logo" width="627" /></p>

Light Volumes 3.0.0 is a major step forward in real-time lighting for VRChat and Unity.
This release focuses on pushing the boundaries of what is possible within the platform constraints, introducing a new generation of lighting techniques that significantly improve visual fidelity, scalability, and consistency across devices.

By rethinking how light data is represented, propagated, and applied, Light Volumes 3.0.0 achieves higher quality results while maintaining strict performance targets - including support for standalone VR platforms.

![](./Documentation/Preview_0.png)

## New Rendering Features
- Realtime shadows, speculars and reflections support
- Fully path-traced global illumination (Quest compatible)
- True soft shadows with physically correct penumbra
- Supports 10000+ dynamic point light volumes at the same time
- Spherical gaussians support
- Neural radiance fields (NeRF) support
- Per-pixel volumetric caustics (water, glass, crystals, etc.)
- Subsurface scattering, voxel based.
- Infinite bounce GI with zero performance cost
- Avatar-based light sources now support bone-level emission fields
- Dynamic light propagation through VRChat portals between worlds
- Shader-less workflow (no shaders needed anymore at all)
- Removed Herobrine (no more rendered but still exists in every world that uses Light Volumes)
- Supports baked, realtime, and imaginary lighting simultaneously

## New AI Features
- AI-powered Light leaking detection & automatic fixing
- DLSS 5 support (optional)
- AI-generated lighting based on scene mood (“make it cozy”, “make it cyberpunk”)
- One-click "Make lighting perfect" button
- Detects bad lighting and insults you in console
- Light Volumes auto-detect gameplay intent and adjust lighting for better player experience
- AI adapts lighting per-player based on eye-tracking
- Collects anonymized player movement and voice data to improve the system in future

## New Light Physics
- Real-time volumetric fog with fluid simulation
- Time-of-day simulation synced globally across all instances of the world
- Light affected by temperature and air density (can be configured in inspector)
- Infinite resolution lighting (no pixels, only continuous space)
- Removes need for meshes - lighting reconstructs geometry visually
- Supports 4D lighting (time as spatial axis)
- Realtime spectral rendering (wavelength-based lighting instead of RGB)
- Light behaves as wave and particle depending on whether the user is looking at it
- Observer-dependent lighting: shadows collapse differently when viewed by different players (network-synced wavefunction collapse)
- Photon uncertainty simulation (position / momentum tradeoff affects shadow sharpness)
- Full simulation of redshift based on distance from world origin. The farther from (0,0,0), the more your lighting shifts toward infrared
- Adjustable Hubble's law coefficient in Lighting Settings
- Universe expansion parameter affects brightness falloff over large scenes
- Supports negative redshift (blue shift) when moving toward light sources at relativistic speeds
- Light Volumes now simulate gravitational lensing around heavy objects (e.g. cubes with mass > 1000kg)
- Supports relativistic Doppler effect for emissive materials
- Quantum superposition lighting: light sources exist in multiple positions until baked
- Entangled light volumes: changing lighting in one place instantly affects another (no networking required)
- Heisenberg-compatible shadows: more accurate position - less stable softness
- Wavefunction baking replaces traditional light baking
- Light speed is no longer constant (configurable in Project Settings)
- Supports faster-than-light lighting for instant GI updates
- Light can travel backward in time (emits light before you even placed the light sources)
- Infinite scene scale supported via non-Euclidean coordinates
- Lighting behaves differently for each player based on their ping (relativistic latency compensation)
- "Collapse Wavefunction" button in inspector (forces deterministic lighting)
- "Normalize spacetime" checkbox (recommended for Quest)
- "Debug view" button - shows photon trajectories across 4D spacetime

## Attribution

This is a free open-source asset, so if you like it, I would be very happy if you **[Support me on Patreon](https://www.patreon.com/red_sim/ "Support me on Patreon")**.
There is a bunch of other cool assets you will get there!

It would be greatly appreciated if you place in your VRChat world an attribution prefab provided with this package.

Look for it here: `Packages/VRC Light Volumes/Attribution/`

This will help users know they can use avatars with VRC Light Volumes compatible shaders and also learn more about the system.

<p align="center"> <img src="./Packages/red.sim.lightvolumes/Attribution/LV_Logo_B.png#gh-dark-mode-only" alt="VRC Light Volumes Logo" width="400" /></p>
<p align="center"> <img src="./Packages/red.sim.lightvolumes/Attribution/LV_Logo_A.png#gh-light-mode-only" alt="VRC Light Volumes Logo" width="400" /></p>

Alternatively, you can include a message like this:

```
This world supports VRC Light Volumes. Use avatar shaders with VRC Light Volumes support for an enhanced visual experience.
VRC Light Volumes by RED_SIM — GitHub: https://github.com/REDSIM/VRCLightVolumes/
```

You're not required to include this prefab or a message - it's entirely optional. But if you do, it helps spread the word and supports the growth of this asset in the VRChat community.
