using UnityEngine;
using System;
using UnityEngine.Serialization;

#if UDONSHARP
using VRC.SDKBase;
using UdonSharp;
#else
using System.Collections;
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

        public const float Version = 2; // VRC Light Volumes Current version. This value used in shaders (_UdonLightVolumeEnabled) to determine which features are can be used
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
        [Tooltip("The minimum brightness at a point due to lighting from an area light, before the area light is culled. Larger values will result in better performance, but may cause artifacts. Setting this to 0 disables distance-based culling for area lights.")]
        public float AreaLightBrightnessCutoff = 0.01f;
        [Tooltip("All Light Volume instances sorted in decreasing order by weight. You can enable or disable volumes game objects at runtime. Manually disabling unnecessary volumes improves performance.")]
        public LightVolumeInstance[] LightVolumeInstances = new LightVolumeInstance[0];
        [Tooltip("All Point Light Volume instances. You can enable or disable point light volumes game objects at runtime. Manually disabling unnecessary point light volumes improves performance.")]
        public PointLightVolumeInstance[] PointLightVolumeInstances = new PointLightVolumeInstance[0];
        [Tooltip("All textures that can be used for as Cubemaps, LUT or Cookies")]
        public Texture2DArray CustomTextures;
        [Tooltip("Cubemaps count that stored in CustomTextures. Cubemap array elements starts from the beginning, 6 elements each.")]
        public int CubemapsCount = 0;

        private bool _isInitialized = false;
#if UDONSHARP
        private bool _isUpdateRequested = false;
#else
        private Coroutine _updateCoroutine = null;
#endif

        // Light Volumes Data
        private int _enabledCount = 0;
        private int _lastEnabledCount = -1;
        private int _additiveCount = 0;

        private Vector4[] _invLocalEdgeSmooth = new Vector4[0];
        private Vector4[] _colors = new Vector4[0];
        private Vector4[] _invWorldMatrix3x4 = new Vector4[0];
        private Vector4[] _boundsUvwScale = new Vector4[0];
        private Vector4[] _relativeRotationQuaternion = new Vector4[0];

        // Point Lights Data
        private int _pointLightCount = 0;
        private int _lastPointLightCount = -1;
        private int[] _enabledPointIDs = new int[128];
        private Vector4[] _pointLightPosition;
        private Vector4[] _pointLightColor;
        private Vector4[] _pointLightDirection;
        private float[] _pointLightCustomId;

        // Legacy support Data
        private Matrix4x4[] _invWorldMatrix = new Matrix4x4[0];
        private Vector4[] _boundsUvw = new Vector4[0];
        private Vector4[] _relativeRotation = new Vector4[0];

        // Other
        private int[] _enabledIDs = new int[32];
        private Vector4[] _boundsScale = new Vector4[3];
        private Vector4[] _bounds = new Vector4[6]; // Legacy

        #region Shader Property IDs
        // Light Volumes
        private int lightVolumeInvLocalEdgeSmoothID;
        private int lightVolumeColorID;
        private int lightVolumeCountID;
        private int lightVolumeAdditiveCountID;
        private int lightVolumeAdditiveMaxOverdrawID;
        private int lightVolumeEnabledID;
        private int lightVolumeProbesBlendID;
        private int lightVolumeSharpBoundsID;
        private int lightVolumeID;
        private int lightVolumeRotationQuaternionID;
        private int lightVolumeInvWorldMatrix3x4ID;
        private int lightVolumeUvwScaleID;
        // Point Lights
        private int _pointLightPositionID;
        private int _pointLightColorID;
        private int _pointLightDirectionID;
        private int _pointLightCustomIdID;
        private int _pointLightCountID;
        private int _pointLightCubeCountID;
        private int _pointLightTextureID;
        private int _areaLightBrightnessCutoffID;
        // Legacy support
        private int lightVolumeRotationID;
        private int lightVolumeInvWorldMatrixID;
        private int lightVolumeUvwID;

        public bool AutoUpdateVolumes {
            get => _autoUpdateVolumes;
            set {
                _autoUpdateVolumes = value;
#if COMPILER_UDONSHARP
                if (_autoUpdateVolumes) RequestUpdateVolumes();
#endif
            }
        }

        private void OnDisable() {
#if !UDONSHARP
            if (_updateCoroutine != null) {
                StopCoroutine(_updateCoroutine);
                _updateCoroutine = null;
            }
#endif
            TryInitialize();
            VRCShader.SetGlobalFloat(lightVolumeEnabledID, 0);
        }

        // Initializing gloabal shader arrays if needed 
        private void TryInitialize() {

#if !UNITY_EDITOR
            if (_isInitialized) return;
#endif
            // Light Volumes
            lightVolumeInvLocalEdgeSmoothID = VRCShader.PropertyToID("_UdonLightVolumeInvLocalEdgeSmooth");
            lightVolumeInvWorldMatrixID = VRCShader.PropertyToID("_UdonLightVolumeInvWorldMatrix");
            lightVolumeUvwID = VRCShader.PropertyToID("_UdonLightVolumeUvw");
            lightVolumeColorID = VRCShader.PropertyToID("_UdonLightVolumeColor");
            lightVolumeCountID = VRCShader.PropertyToID("_UdonLightVolumeCount");
            lightVolumeAdditiveCountID = VRCShader.PropertyToID("_UdonLightVolumeAdditiveCount");
            lightVolumeAdditiveMaxOverdrawID = VRCShader.PropertyToID("_UdonLightVolumeAdditiveMaxOverdraw");
            lightVolumeEnabledID = VRCShader.PropertyToID("_UdonLightVolumeEnabled");
            lightVolumeProbesBlendID = VRCShader.PropertyToID("_UdonLightVolumeProbesBlend");
            lightVolumeSharpBoundsID = VRCShader.PropertyToID("_UdonLightVolumeSharpBounds");
            lightVolumeID = VRCShader.PropertyToID("_UdonLightVolume");
            lightVolumeRotationQuaternionID = VRCShader.PropertyToID("_UdonLightVolumeRotationQuaternion");
            lightVolumeInvWorldMatrix3x4ID = VRCShader.PropertyToID("_UdonLightVolumeInvWorldMatrix3x4");
            lightVolumeUvwScaleID = VRCShader.PropertyToID("_UdonLightVolumeUvwScale");
            // Point Lights
            _pointLightPositionID = VRCShader.PropertyToID("_UdonPointLightVolumePosition");
            _pointLightColorID = VRCShader.PropertyToID("_UdonPointLightVolumeColor");
            _pointLightDirectionID = VRCShader.PropertyToID("_UdonPointLightVolumeDirection");
            _pointLightCountID = VRCShader.PropertyToID("_UdonPointLightVolumeCount");
            _pointLightCustomIdID = VRCShader.PropertyToID("_UdonPointLightVolumeCustomID");
            _pointLightCubeCountID = VRCShader.PropertyToID("_UdonPointLightVolumeCubeCount");
            _pointLightTextureID = VRCShader.PropertyToID("_UdonPointLightVolumeTexture");
            _areaLightBrightnessCutoffID = VRCShader.PropertyToID("_UdonAreaLightBrightnessCutoff");
            // Legacy support
            lightVolumeRotationID = VRCShader.PropertyToID("_UdonLightVolumeRotation");
            lightVolumeInvWorldMatrixID = VRCShader.PropertyToID("_UdonLightVolumeInvWorldMatrix");
            lightVolumeUvwID = VRCShader.PropertyToID("_UdonLightVolumeUvw");

#if UNITY_EDITOR
            if (_isInitialized) return;
#endif
            // Light Volumes
            VRCShader.SetGlobalVectorArray(lightVolumeInvLocalEdgeSmoothID, new Vector4[32]);
            VRCShader.SetGlobalVectorArray(lightVolumeColorID, new Vector4[32]);
            VRCShader.SetGlobalVectorArray(lightVolumeInvWorldMatrix3x4ID, new Vector4[96]);
            VRCShader.SetGlobalVectorArray(lightVolumeRotationQuaternionID, new Vector4[32]);
            VRCShader.SetGlobalVectorArray(lightVolumeUvwScaleID, new Vector4[96]);
            // Point Lights
            VRCShader.SetGlobalVectorArray(_pointLightPositionID, new Vector4[128]);
            VRCShader.SetGlobalVectorArray(_pointLightColorID, new Vector4[128]);
            VRCShader.SetGlobalVectorArray(_pointLightDirectionID, new Vector4[128]);
            VRCShader.SetGlobalFloatArray(_pointLightCustomIdID, new float[128]);
            // Legacy support
            VRCShader.SetGlobalMatrixArray(lightVolumeInvWorldMatrixID, new Matrix4x4[32]);
            VRCShader.SetGlobalVectorArray(lightVolumeRotationID, new Vector4[64]);
            VRCShader.SetGlobalVectorArray(lightVolumeUvwID, new Vector4[192]);

            _isInitialized = true;
        }

        #endregion

        private void OnEnable() {
            if (!AutoUpdateVolumes) return;
            RequestUpdateVolumes();
        }

        private void Start() {
            _isInitialized = false;
            UpdateVolumes();
        }

        public void RequestUpdateVolumes() {
#if UDONSHARP
            if (_isUpdateRequested) return; // Prevent multiple requests
            _isUpdateRequested = true;
            SendCustomEventDelayedFrames(nameof(DeferredUpdateVolumes), 1);
#else
            if (_updateCoroutine != null || !isActiveAndEnabled) return;
            _updateCoroutine = StartCoroutine(DeferredUpdateVolumes());
#endif
        }

#if UDONSHARP
        public void DeferredUpdateVolumes() {
            if (AutoUpdateVolumes && enabled && gameObject.activeInHierarchy) {
                SendCustomEventDelayedFrames(nameof(DeferredUpdateVolumes), 1); // Auto schedule next update if AutoUpdateVolumes is enabled
            } else {
                _isUpdateRequested = false;
            }
            UpdateVolumes();
        }
#else
        private IEnumerator DeferredUpdateVolumes() {
            do {
                yield return null;
                UpdateVolumes();
            } while (AutoUpdateVolumes);
            _updateCoroutine = null;
        }
#endif

        public void UpdateVolumes() {

            TryInitialize();

            if (!enabled || !gameObject.activeInHierarchy) {
                VRCShader.SetGlobalFloat(lightVolumeEnabledID, 0);
                return;
            }

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

            // Initializing required arrays
            if (_enabledCount != _lastEnabledCount) {
                _invLocalEdgeSmooth = new Vector4[_enabledCount];
                _invWorldMatrix3x4 = new Vector4[_enabledCount * 3];
                _relativeRotationQuaternion = new Vector4[_enabledCount];
                _boundsUvwScale = new Vector4[_enabledCount * 3];
                _colors = new Vector4[_enabledCount];

                // Legacy data arrays
                _invWorldMatrix = new Matrix4x4[_enabledCount];
                _relativeRotation = new Vector4[_enabledCount * 2];
                _boundsUvw = new Vector4[_enabledCount * 6];
                _lastEnabledCount = _enabledCount;
            }

            // Filling arrays with enabled volumes
            for (int i = 0; i < _enabledCount; i++) {

                int enabledId = _enabledIDs[i];
                int i2 = i * 2;
                int i3 = i * 3;
                int i6 = i * 6;

                LightVolumeInstance instance = LightVolumeInstances[enabledId];

                // Setting volume transform
                var invWorldMatrix = instance.InvWorldMatrix;
                _invWorldMatrix3x4[i3] = invWorldMatrix.GetRow(0);
                _invWorldMatrix3x4[i3 + 1] = invWorldMatrix.GetRow(1);
                _invWorldMatrix3x4[i3 + 2] = invWorldMatrix.GetRow(2);
                _invWorldMatrix[i] = invWorldMatrix; // Legacy

                _invLocalEdgeSmooth[i] = instance.InvLocalEdgeSmoothing; // Setting volume edge smoothing

                Vector4 c = instance.Color; // Changing volume color
                c.w = instance.IsRotated ? 1 : 0; // Color alpha stores if volume rotated or not
                _colors[i] = c;

                // Setting volume relative rotation
                _relativeRotationQuaternion[i] = instance.RelativeRotation;
                _relativeRotation[i2] = instance.RelativeRotationRow0; // Legacy
                _relativeRotation[i2 + 1] = instance.RelativeRotationRow1; // Legacy

                // Setting volume UVW bounds
                _boundsScale[0] = instance.BoundsUvwMin0;
                _boundsScale[1] = instance.BoundsUvwMin1;
                _boundsScale[2] = instance.BoundsUvwMin2;
                // Legacy
                _bounds[0] = instance.BoundsUvwMin0;
                _bounds[1] = instance.BoundsUvwMax0;
                _bounds[2] = instance.BoundsUvwMin1;
                _bounds[3] = instance.BoundsUvwMax1;
                _bounds[4] = instance.BoundsUvwMin2;
                _bounds[5] = instance.BoundsUvwMax2;

                Array.Copy(_boundsScale, 0, _boundsUvwScale, i3, 3);
                Array.Copy(_bounds, 0, _boundsUvw, i6, 6); // Legacy

            }

            // Searching for enabled point light volumes
            _pointLightCount = 0;
            int pointMaxLength = Mathf.Min(PointLightVolumeInstances.Length, 128);
            for (int i = 0; i < pointMaxLength; i++) {
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                if (instance == null) continue;
                instance.UpdateNotifier = this; // Setting update notifier for the instance
                if (instance.gameObject.activeInHierarchy) {
#if UNITY_EDITOR
                    instance.UpdateTransform();
#else
                    if (instance.IsDynamic) instance.UpdateTransform();
#endif
                    _enabledPointIDs[_pointLightCount] = i;
                    _pointLightCount++;
                }
            }

            // Initializing required arrays
            if (_pointLightCount != _lastPointLightCount) {
                _pointLightPosition = new Vector4[_pointLightCount];
                _pointLightColor = new Vector4[_pointLightCount];
                _pointLightDirection = new Vector4[_pointLightCount];
                _pointLightCustomId = new float[_pointLightCount];
                _lastPointLightCount = _pointLightCount;
            }

            // Filling arrays with enabled point light volumes
            for (int i = 0; i < _pointLightCount; i++) {
                PointLightVolumeInstance instance = PointLightVolumeInstances[_enabledPointIDs[i]];
                _pointLightPosition[i] = instance.PositionData;
                _pointLightColor[i] = instance.ColorData;
                _pointLightDirection[i] = instance.DirectionData;
                _pointLightCustomId[i] = instance.CustomID;
            }

            bool isAtlas = LightVolumeAtlas != null;

            // Disabling light volumes system if no atlas or no volumes
            if ((!isAtlas || _enabledCount == 0) && _pointLightCount == 0) {
                VRCShader.SetGlobalFloat(lightVolumeEnabledID, 0);
                return;
            }

            // 3D texture and it's parameters
            if (isAtlas) {
                VRCShader.SetGlobalTexture(lightVolumeID, LightVolumeAtlas);
            }

            // Regular Light Volumes
            VRCShader.SetGlobalFloat(lightVolumeCountID, _enabledCount);
            VRCShader.SetGlobalFloat(lightVolumeAdditiveCountID, _additiveCount);
            if (_enabledCount != 0) {

                // Defines if Light Probes Blending enabled in scene
                VRCShader.SetGlobalFloat(lightVolumeProbesBlendID, LightProbesBlending ? 1 : 0);
                VRCShader.SetGlobalFloat(lightVolumeSharpBoundsID, SharpBounds ? 1 : 0);

                // All light volumes inv Edge smooth
                VRCShader.SetGlobalVectorArray(lightVolumeInvLocalEdgeSmoothID, _invLocalEdgeSmooth);

                // All light volumes UVW
                VRCShader.SetGlobalVectorArray(lightVolumeUvwScaleID, _boundsUvwScale);

                // Volume Transform Matrix
                VRCShader.SetGlobalVectorArray(lightVolumeInvWorldMatrix3x4ID, _invWorldMatrix3x4);

                // Max Overdraw
                VRCShader.SetGlobalFloat(lightVolumeAdditiveMaxOverdrawID, AdditiveMaxOverdraw);

                // Volume's relative rotation
                VRCShader.SetGlobalVectorArray(lightVolumeRotationQuaternionID, _relativeRotationQuaternion);

                // Volume's color correction
                VRCShader.SetGlobalVectorArray(lightVolumeColorID, _colors);

                // Legacy data setting
                VRCShader.SetGlobalMatrixArray(lightVolumeInvWorldMatrixID, _invWorldMatrix);
                VRCShader.SetGlobalVectorArray(lightVolumeUvwID, _boundsUvw);
                VRCShader.SetGlobalVectorArray(lightVolumeRotationID, _relativeRotation);
            }

            // Point Lights
            VRCShader.SetGlobalFloat(_pointLightCountID, _pointLightCount);
            VRCShader.SetGlobalFloat(_pointLightCubeCountID, CubemapsCount);
            if (_pointLightCount != 0) {
                VRCShader.SetGlobalVectorArray(_pointLightColorID, _pointLightColor);
                VRCShader.SetGlobalVectorArray(_pointLightPositionID, _pointLightPosition);
                VRCShader.SetGlobalVectorArray(_pointLightDirectionID, _pointLightDirection);
                VRCShader.SetGlobalFloatArray(_pointLightCustomIdID, _pointLightCustomId);
                VRCShader.SetGlobalFloat(_areaLightBrightnessCutoffID, AreaLightBrightnessCutoff);
            }
            if(CustomTextures != null) {
                VRCShader.SetGlobalTexture(_pointLightTextureID, CustomTextures);
            }

            // Defines if Light Volumes enabled in scene. 0 if disabled. And a version number if enabled
            VRCShader.SetGlobalFloat(lightVolumeEnabledID, Version);

        }
    }
}