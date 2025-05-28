using UnityEngine;
using System;
using UnityEngine.Serialization;

#if UDONSHARP
using VRC.SDKBase;
using UdonSharp;
#else
using VRCShader = UnityEngine.Shader;
#endif

namespace VRCLightVolumes {
#if UDONSHARP
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LightVolumeManager : UdonSharpBehaviour
#else
    public class LightVolumeManager : MonoBehaviour
#endif
    {

        [Tooltip("Combined Texture3D containing all Light Volumes' textures.")]
        public Texture3D LightVolumeAtlas;
        [Tooltip("When enabled, areas outside Light Volumes fall back to light probes. Otherwise, the Light Volume with the smallest weight is used as fallback. It also improves performance.")]
        public bool LightProbesBlending = true;
        [Tooltip("Disables smooth blending with areas outside Light Volumes. Use it if your entire scene's play area is covered by Light Volumes. It also improves performance.")]
        public bool SharpBounds = true;
        [FormerlySerializedAs("AutoUpdateVolumes")]
        [FieldChangeCallback(nameof(AutoUpdateVolumes))]
        [Tooltip("Automatically updates any volumes data in runtime: Enabling/Disabling, Color, Edge Smoothing, all the global settings and more. Position, Rotation and Scale gets updated only for volumes that are marked dynamic.")]
        public bool _autoUpdateVolumes = false;
        [Tooltip("Limits the maximum number of additive volumes that can affect a single pixel. If you have many dynamic additive volumes that may overlap, it's good practice to limit overdraw to maintain performance.")]
        public int AdditiveMaxOverdraw = 4;
        [Tooltip("All Light Volume instances sorted in decreasing order by weight. You can enable or disable volumes game objects at runtime. Manually disabling unnecessary volumes improves performance.")]
        public LightVolumeInstance[] LightVolumeInstances = new LightVolumeInstance[0];

        private bool _isInitialized = false;
        private bool _isUpdateRequested = false;

        // Actually enabled Volumes
        private int _enabledCount = 0;
        private int[] _enabledIDs = new int[32];
        private Vector4[] _invLocalEdgeSmooth = new Vector4[0];
        private Matrix4x4[] _invWorldMatrix = new Matrix4x4[0];
        private Vector4[] _boundsUvw = new Vector4[0];
        private Vector4[] _relativeRotations = new Vector4[0];
        private Vector4[] _colors = new Vector4[0];
        private int _additiveCount = 0;
        private Vector4[] _bounds = new Vector4[6];

        private int lightVolumeInvLocalEdgeSmoothID;
        private int lightVolumeInvWorldMatrixID;
        private int lightVolumeUvwID;
        private int lightVolumeColorID;
        private int lightVolumeRotationID;
        private int lightVolumeCountID;
        private int lightVolumeAdditiveCountID;
        private int lightVolumeAdditiveMaxOverdrawID;
        private int lightVolumeEnabledID;
        private int lightVolumeProbesBlendID;
        private int lightVolumeSharpBoundsID;
        private int lightVolumeID;

        public bool AutoUpdateVolumes {
            get => _autoUpdateVolumes;
            set {
                _autoUpdateVolumes = value;
#if COMPILER_UDONSHARP
                if (_autoUpdateVolumes) RequestUpdateVolumes();
#endif
            }
        }

        // Initializing gloabal shader arrays if needed 
        private void TryInitialize() {

#if !UNITY_EDITOR
            if (_isInitialized) return;
#endif

            lightVolumeInvLocalEdgeSmoothID = VRCShader.PropertyToID("_UdonLightVolumeInvLocalEdgeSmooth");
            lightVolumeInvWorldMatrixID = VRCShader.PropertyToID("_UdonLightVolumeInvWorldMatrix");
            lightVolumeUvwID = VRCShader.PropertyToID("_UdonLightVolumeUvw");
            lightVolumeColorID = VRCShader.PropertyToID("_UdonLightVolumeColor");
            lightVolumeRotationID = VRCShader.PropertyToID("_UdonLightVolumeRotation");
            lightVolumeCountID = VRCShader.PropertyToID("_UdonLightVolumeCount");
            lightVolumeAdditiveCountID = VRCShader.PropertyToID("_UdonLightVolumeAdditiveCount");
            lightVolumeAdditiveMaxOverdrawID = VRCShader.PropertyToID("_UdonLightVolumeAdditiveMaxOverdraw");
            lightVolumeEnabledID = VRCShader.PropertyToID("_UdonLightVolumeEnabled");
            lightVolumeProbesBlendID = VRCShader.PropertyToID("_UdonLightVolumeProbesBlend");
            lightVolumeSharpBoundsID = VRCShader.PropertyToID("_UdonLightVolumeSharpBounds");
            lightVolumeID = VRCShader.PropertyToID("_UdonLightVolume");

#if UNITY_EDITOR
            if (_isInitialized) return;
#endif

            VRCShader.SetGlobalVectorArray(lightVolumeInvLocalEdgeSmoothID, new Vector4[32]);
            VRCShader.SetGlobalMatrixArray(lightVolumeInvWorldMatrixID, new Matrix4x4[32]);
            VRCShader.SetGlobalVectorArray(lightVolumeRotationID, new Vector4[64]);
            VRCShader.SetGlobalVectorArray(lightVolumeUvwID, new Vector4[192]);
            VRCShader.SetGlobalVectorArray(lightVolumeColorID, new Vector4[32]);
            _isInitialized = true;
        }

        private void OnEnable() {
            if (!AutoUpdateVolumes) return;
            RequestUpdateVolumes();
        }

        private void Start() {
            _isInitialized = false;
            UpdateVolumes();
        }

        public void RequestUpdateVolumes() {
            if (_isUpdateRequested) return; // Prevent multiple requests
            _isUpdateRequested = true;
            SendCustomEventDelayedFrames(nameof(DeferredUpdateVolumes), 1);
        }

        public void DeferredUpdateVolumes() {
            if (AutoUpdateVolumes && enabled && gameObject.activeInHierarchy) {
                SendCustomEventDelayedFrames(nameof(DeferredUpdateVolumes), 1); // Auto schedule next update if AutoUpdateVolumes is enabled
            } else {
                _isUpdateRequested = false;
            }
            UpdateVolumes();
        }

        public void UpdateVolumes() {

            TryInitialize();

            // Searching for enabled volumes. Counting Additive volumes.
            int enabledCount = 0;
            _additiveCount = 0;
            int maxLength = Mathf.Min(LightVolumeInstances.Length, 32);
            for (int i = 0; i < maxLength; i++) {
                LightVolumeInstance instance = LightVolumeInstances[i];
                if (instance == null) continue;
                instance.UpdateNotifier = this; // Setting update notifier for the instance
                if (instance.enabled && instance.gameObject.activeInHierarchy) {
#if UNITY_EDITOR
                    instance.UpdateTransform();
#else
                    if (instance.IsDynamic) instance.UpdateTransform();
#endif
                    if (instance.IsAdditive) _additiveCount++;
                    _enabledIDs[enabledCount] = i;
                    enabledCount++;
                }
            }

            // Resize arrays if needed
            if (enabledCount != _enabledCount) {
                _enabledCount = enabledCount;
                _invLocalEdgeSmooth = new Vector4[_enabledCount];
                _invWorldMatrix = new Matrix4x4[_enabledCount];
                _colors = new Vector4[_enabledCount];
                _relativeRotations = new Vector4[_enabledCount * 2];
                _boundsUvw = new Vector4[_enabledCount * 6];
            }

            // Filling arrays with enabled volumes
            for (int i = 0; i < _enabledCount; i++) {

                int enabledId = _enabledIDs[i];
                int i2 = i * 2;
                int i6 = i * 6;

                LightVolumeInstance instance = LightVolumeInstances[enabledId];

                _invLocalEdgeSmooth[i] = instance.InvLocalEdgeSmoothing; // Setting volume edge smoothing
                _invWorldMatrix[i] = instance.InvWorldMatrix; // Setting volume transform

                Vector4 c = instance.Color; // Changing volume color
                c.w = instance.IsRotated ? 1 : 0; // Color alpha stores if volume rotated or not
                _colors[i] = c;

                // Setting volume relative rotation as 3x2 matrix
                _relativeRotations[i2] = instance.RelativeRotationRow0;
                _relativeRotations[i2 + 1] = instance.RelativeRotationRow1;

                // Setting volume UVW bounds
                _bounds[0] = instance.BoundsUvwMin0;
                _bounds[1] = instance.BoundsUvwMax0;
                _bounds[2] = instance.BoundsUvwMin1;
                _bounds[3] = instance.BoundsUvwMax1;
                _bounds[4] = instance.BoundsUvwMin2;
                _bounds[5] = instance.BoundsUvwMax2;
                Array.Copy(_bounds, 0, _boundsUvw, i6, 6);

            }

            // Disabling light volumes system if no atlas or no volumes
            if (LightVolumeAtlas == null || _enabledCount == 0) {
                VRCShader.SetGlobalFloat(lightVolumeEnabledID, 0);
                return;
            }

            // 3D texture and it's parameters
            VRCShader.SetGlobalTexture(lightVolumeID, LightVolumeAtlas);

            // Defines if Light Probes Blending enabled in scene
            VRCShader.SetGlobalFloat(lightVolumeProbesBlendID, LightProbesBlending ? 1 : 0);
            VRCShader.SetGlobalFloat(lightVolumeSharpBoundsID, SharpBounds ? 1 : 0);

            // All light volumes Extra Data
            VRCShader.SetGlobalVectorArray(lightVolumeInvLocalEdgeSmoothID, _invLocalEdgeSmooth);
            VRCShader.SetGlobalMatrixArray(lightVolumeInvWorldMatrixID, _invWorldMatrix);

            // All light volumes UVW
            VRCShader.SetGlobalVectorArray(lightVolumeUvwID, _boundsUvw);

            // All light volumes count
            VRCShader.SetGlobalFloat(lightVolumeCountID, _enabledCount);
            VRCShader.SetGlobalFloat(lightVolumeAdditiveCountID, _additiveCount);
            VRCShader.SetGlobalFloat(lightVolumeAdditiveMaxOverdrawID, Mathf.Min(Mathf.Max(AdditiveMaxOverdraw, 0), _additiveCount));

            // Defines if Light Volumes enabled in scene
            VRCShader.SetGlobalFloat(lightVolumeEnabledID, 1);

            // Volume's relative rotation
            VRCShader.SetGlobalVectorArray(lightVolumeRotationID, _relativeRotations);

            // Volume's color correction
            VRCShader.SetGlobalVectorArray(lightVolumeColorID, _colors);

        }
    }
}