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
        [Tooltip("Continuously asks the target Point Light Volume to update shadow faces through a delayed Udon event loop.")]
        public bool Realtime = false;
        [Tooltip("How many cubemap faces the target Point Light Volume renders per realtime bake tick.")]
        [Range(1, 6)] public int RealtimeFacesPerFrame = 1;

        private PointLightVolumeInstance _configuredTargetPointLightVolume;
        private int _configuredFacesPerFrame = -1;
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

        // Clears cached target configuration. A queued Udon event owns the scheduled flag until it runs. Keeping the flag prevents a quick disable-enable cycle from creating a second loop.
        private void OnDisable() {
            _configuredTargetPointLightVolume = null;
        }

#if !UDONSHARP
        // Drives the target point light once per frame in regular Unity runtime.
        private void Update() {
            if (!Realtime || TargetPointLightVolume == null) return;
            PointLightVolumeInstance target = TargetPointLightVolume;
            ConfigureTargetBake(target, RealtimeFacesPerFrame, true);
            target.BakeShadows();
        }
#endif

        // Writes one-shot bake fields to the target point light instance and triggers its native runtime bake.
        public void BakeShadows() {
            if (TargetPointLightVolume == null) return;
            PointLightVolumeInstance target = TargetPointLightVolume;
            ConfigureTargetBake(target, 6, false);
            target.BakeShadows();
        }

        // Delayed Udon realtime loop that only triggers the target point light.
        public void _RealtimeBakeLoop() {
#if UDONSHARP
            _realtimeLoopScheduled = false;
#endif
            if (!enabled || !gameObject.activeInHierarchy || !Realtime) return;
            if (TargetPointLightVolume != null) {
                PointLightVolumeInstance target = TargetPointLightVolume;
                ConfigureTargetBake(target, RealtimeFacesPerFrame, true);
                target.BakeShadows();
            }
#if UDONSHARP
            _realtimeLoopScheduled = true;
            SendCustomEventDelayedFrames(nameof(_RealtimeBakeLoop), 1);
#endif
        }

        // Writes realtime bake fields to the target and starts the external trigger loop.
        private void StartTargetRealtimeBakeLoop() {
            if (TargetPointLightVolume == null) return;
            ConfigureTargetBake(TargetPointLightVolume, RealtimeFacesPerFrame, true);
#if UDONSHARP
            if (_realtimeLoopScheduled) return;
            _realtimeLoopScheduled = true;
            SendCustomEventDelayedFrames(nameof(_RealtimeBakeLoop), 1);
#endif
        }

        // Applies only external scheduling settings; resolution and blur quality remain owned by the target light.
        private void ConfigureTargetBake(PointLightVolumeInstance target, int facesPerFrame, bool directOutput) {
            if (_configuredTargetPointLightVolume == target && _configuredFacesPerFrame == facesPerFrame && _configuredDirectOutput == directOutput && target.RuntimeShadowFacesPerFrame == facesPerFrame && target.RuntimeShadowDirectOutput == directOutput) return;
            target.RuntimeShadowFacesPerFrame = facesPerFrame;
            target.RuntimeShadowDirectOutput = directOutput;
            _configuredTargetPointLightVolume = target;
            _configuredFacesPerFrame = facesPerFrame;
            _configuredDirectOutput = directOutput;
        }
    }
}
