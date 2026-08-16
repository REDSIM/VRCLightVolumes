#if !UDONSHARP && (UNITY_EDITOR || COMPILER_UDONSHARP)
#define UDONSHARP
#endif

using UnityEngine;

#if UDONSHARP
using UdonSharp;
#endif

namespace VRCLightVolumes {
    [DisallowMultipleComponent]
#if UDONSHARP
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PointLightShadowRuntimeBaker : UdonSharpBehaviour
#else
    public class PointLightShadowRuntimeBaker : MonoBehaviour
#endif
    {
        [Tooltip("Point Light Volume instance that receives the runtime-baked shadow texture.")]
        public PointLightVolumeInstance TargetPointLightVolume;
        [Tooltip("Bake one full shadow cubemap when this behaviour is enabled.")]
        public bool BakeOnEnable = true;
        [Tooltip("Continuously bakes the target's complete shadow directly into the Manager atlas through a delayed Udon event loop.")]
        public bool Realtime = false;

        private PointLightVolumeInstance _configuredTargetPointLightVolume;
        private bool _configuredDirectOutput = false;
#if UDONSHARP
        private bool _realtimeLoopScheduled = false;
#else
        private bool _hasStarted = false;
#endif

#if !UDONSHARP
        // Starts runtime baking once regular MonoBehaviour startup order is stable.
        private void Start() {
            _hasStarted = true;
            if (Realtime) StartTargetRealtimeBakeLoop();
            else if (BakeOnEnable) BakeShadows();
        }
#endif

        // Starts one-shot or realtime target baking when this external baker becomes active.
        private void OnEnable() {
#if UDONSHARP
            if (Realtime) StartTargetRealtimeBakeLoop();
            else if (BakeOnEnable) BakeShadows();
#else
            if (!_hasStarted) return;
            if (Realtime) StartTargetRealtimeBakeLoop();
            else if (BakeOnEnable) BakeShadows();
#endif
        }

        // Releases retained scratch resources. A queued Udon event owns the scheduled flag until it
        // runs; keeping the flag prevents a quick disable-enable cycle from creating a second loop.
        private void OnDisable() {
            ReleaseConfiguredTarget();
        }

#if !UDONSHARP
        // Drives the target point light once per frame in regular Unity runtime.
        private void Update() {
            if (!Realtime || TargetPointLightVolume == null) {
                ReleaseConfiguredTarget();
                return;
            }
            PointLightVolumeInstance target = TargetPointLightVolume;
            ConfigureTargetBake(target, true);
            if (target.IsActive) target.BakeShadows();
        }
#endif

        // Writes one-shot bake fields to the target point light instance and triggers its native runtime bake.
        public void BakeShadows() {
            if (TargetPointLightVolume == null) {
                ReleaseConfiguredTarget();
                return;
            }
            PointLightVolumeInstance target = TargetPointLightVolume;
            ConfigureTargetBake(target, false);
            target.BakeShadows();
        }

        // Delayed Udon realtime loop that only triggers the target point light.
        public void _RealtimeBakeLoop() {
#if UDONSHARP
            _realtimeLoopScheduled = false;
#endif
            if (!enabled || !gameObject.activeInHierarchy || !Realtime) {
                ReleaseConfiguredTarget();
                return;
            }
            if (TargetPointLightVolume != null) {
                PointLightVolumeInstance target = TargetPointLightVolume;
                ConfigureTargetBake(target, true);
                if (target.IsActive) target.BakeShadows();
            } else ReleaseConfiguredTarget();
#if UDONSHARP
            _realtimeLoopScheduled = true;
            SendCustomEventDelayedFrames(nameof(_RealtimeBakeLoop), 1);
#endif
        }

        // Writes realtime bake fields to the target and starts the external trigger loop.
        private void StartTargetRealtimeBakeLoop() {
            if (TargetPointLightVolume == null) {
                ReleaseConfiguredTarget();
                return;
            }
            ConfigureTargetBake(TargetPointLightVolume, true);
#if UDONSHARP
            if (_realtimeLoopScheduled) return;
            _realtimeLoopScheduled = true;
            SendCustomEventDelayedFrames(nameof(_RealtimeBakeLoop), 1);
#endif
        }

        // Selects only the output path; resolution and blur settings remain owned by the target light.
        private void ConfigureTargetBake(PointLightVolumeInstance target, bool directOutput) {
            if (_configuredTargetPointLightVolume != target) ReleaseConfiguredTarget();
            if (_configuredTargetPointLightVolume == target && _configuredDirectOutput == directOutput) return;
            target.RuntimeShadowDirectOutput = directOutput;
            _configuredTargetPointLightVolume = target;
            _configuredDirectOutput = directOutput;
        }

        // Gives retained camera and blur scratch back when realtime baking stops or changes targets.
        private void ReleaseConfiguredTarget() {
            PointLightVolumeInstance target = _configuredTargetPointLightVolume;
            if (target != null) target._ReleaseRuntimeShadowBakeResources();
            _configuredTargetPointLightVolume = null;
            _configuredDirectOutput = false;
        }
    }
}
