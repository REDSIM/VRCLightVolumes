#if UNITY_EDITOR && !COMPILER_UDONSHARP
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("red.sim.LightVolumesEditor")]
[assembly: InternalsVisibleTo("red.sim.LightVolumes")]
[assembly: InternalsVisibleTo("red.sim.LightVolumesUdon.EditorTests")]
#endif
