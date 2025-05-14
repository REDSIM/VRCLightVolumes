using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace VRCLightVolumes {
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LightVolumeManager : UdonSharpBehaviour {

        [Tooltip("Combined Texture3D containing all Light Volumes' textures.")]
        public Texture3D LightVolumeAtlas;
        [Tooltip("When enabled, areas outside Light Volumes fall back to light probes. Otherwise, the Light Volume with the smallest weight is used as fallback. It also improves performance.")]
        public bool LightProbesBlending = true;
        [Tooltip("Disables smooth blending with areas outside Light Volumes. Use it if your entire scene's play area is covered by Light Volumes. It also improves performance.")]
        public bool SharpBounds = true;
        [Tooltip("Automatically updates a volume's position, rotation, and scale in Play mode using an Udon script. Use only if you have movable volumes in your scene.")]
        public bool AutoUpdateVolumes = false;
        [Tooltip("Limits the maximum number of additive volumes that can affect a single pixel. If you have many dynamic additive volumes that may overlap, it's good practice to limit overdraw to maintain performance.")]
        public int AdditiveMaxOverdraw = 4;
        [Tooltip("All Light Volume instances sorted in decreasing order by weight. You can enable or disable volumes game objects at runtime. Manually disabling unnecessary volumes improves performance.")]
        public LightVolumeInstance[] LightVolumeInstances = new LightVolumeInstance[0];
        private bool _isInitialized = false;

        public CustomRenderTexture DataCRT;
        public CustomRenderTexture MatrixCRT;
        public CustomRenderTexture SmoothingCRT;

        // Actually enabled Volumes
        private int _enabledCount = 0;
        private int[] _enabledIDs = new int[256];
        private Vector4[] _invLocalEdgeSmooth = new Vector4[0];
        private Matrix4x4[] _invWorldMatrix = new Matrix4x4[0];
        private Vector4[] _boundsUvwMin = new Vector4[0];
        private Vector4[] _boundsUvwMax = new Vector4[0];
        private float[] _isRotated = new float[0];
        private Vector4[] _relativeRotations = new Vector4[0];
        private Vector4[] _colors = new Vector4[0];
        private int _additiveCount = 0;

        // Property IDs
        int _invLocalEdgeSmoothID = 0;
        int _invWorldMatrixID = 0;
        int _relativeRotationID = 0;
        int _isRotatedID = 0;
        int _uvwMinID = 0;
        int _uvwMaxID = 0;
        int _colorID = 0;

        int _enabledID = 0;
        int _enabledCountID = 0;
        int _additiveCountID = 0;
        int _additiveMaxOverdrawID = 0;
        int _lightProbesBlendingID = 0;
        int _sharpBoundsID = 0;

        int _lightVolumeAtlasID = 0;
        int _lightVolumeDataID = 0;
        int _lightVolumeMatrixID = 0;
        int _lightVolumeSmoothingID = 0;

        // Initializing gloabal shader arrays if needed 
        private void TryInitialize() {
            //if (_isInitialized) return;

            // Arrays
            _invLocalEdgeSmoothID = VRCShader.PropertyToID("InvLocalEdgeSmooth");
            _invWorldMatrixID = VRCShader.PropertyToID("InvWorldMatrix");
            _relativeRotationID = VRCShader.PropertyToID("Rotation");
            _isRotatedID = VRCShader.PropertyToID("IsRotated");
            _uvwMinID = VRCShader.PropertyToID("UvwMin");
            _uvwMaxID = VRCShader.PropertyToID("UvwMax");
            _colorID = VRCShader.PropertyToID("Colors");

            // Single Variables
            _enabledID = VRCShader.PropertyToID("_UdonLightVolumeEnabled");
            _enabledCountID = VRCShader.PropertyToID("_UdonLightVolumeEnabledCount");
            _additiveCountID = VRCShader.PropertyToID("_UdonLightVolumeAdditiveCount");
            _additiveMaxOverdrawID = VRCShader.PropertyToID("_UdonLightVolumeAdditiveMaxOverdraw");
            _lightProbesBlendingID = VRCShader.PropertyToID("_UdonLightVolumeProbesBlend");
            _sharpBoundsID = VRCShader.PropertyToID("_UdonLightVolumeSharpBounds");
            _lightVolumeAtlasID = VRCShader.PropertyToID("_UdonLightVolumeAtlas");
            _lightVolumeDataID = VRCShader.PropertyToID("_UdonLightVolumeData");
            _lightVolumeMatrixID = VRCShader.PropertyToID("_UdonLightVolumeMatrix");
            _lightVolumeSmoothingID = VRCShader.PropertyToID("_UdonLightVolumeSmoothing");

            _isInitialized = true;
        }

        private void Update() {
            if (!AutoUpdateVolumes) return;
            UpdateVolumes();
        }

        // Recalculates dynamic volumes
        private void UpdateDynamicVolumes() {

            // Searching for enabled volumes
            _enabledCount = 0;
            _additiveCount = 0;
            for (int i = 0; i < LightVolumeInstances.Length; i++) {
                if (LightVolumeInstances[i] != null && LightVolumeInstances[i].gameObject.activeInHierarchy) {
#if UNITY_EDITOR
                LightVolumeInstances[i].UpdateRotation();
#else
                    if (LightVolumeInstances[i].IsDynamic) LightVolumeInstances[i].UpdateRotation();
#endif
                    if (LightVolumeInstances[i].IsAdditive) _additiveCount++;
                    _enabledIDs[_enabledCount] = i;
                    _enabledCount++;
                }
            }

            // Initializing required arrays
            _invLocalEdgeSmooth = new Vector4[_enabledCount];
            _invWorldMatrix = new Matrix4x4[_enabledCount];
            _isRotated = new float[_enabledCount];
            _colors = new Vector4[_enabledCount];
            _relativeRotations = new Vector4[_enabledCount];
            _boundsUvwMin = new Vector4[_enabledCount * 3];
            _boundsUvwMax = new Vector4[_enabledCount * 3];

            // Filling arrays with enabled volumes
            for (int i = 0; i < _enabledCount; i++) {

                int enabledId = _enabledIDs[i];
                int i3 = i * 3;
                int i31 = i3 + 1;
                int i32 = i3 + 2;

                _invLocalEdgeSmooth[i] = LightVolumeInstances[enabledId].InvLocalEdgeSmoothing;
                _invWorldMatrix[i] = LightVolumeInstances[enabledId].InvWorldMatrix;
                _isRotated[i] = LightVolumeInstances[enabledId].IsRotated ? 1 : 0;
                _relativeRotations[i] = LightVolumeInstances[enabledId].RelativeRotation;
                _colors[i] = LightVolumeInstances[enabledId].Color;

                _boundsUvwMin[i3] = LightVolumeInstances[enabledId].BoundsUvwMin0;
                _boundsUvwMin[i31] = LightVolumeInstances[enabledId].BoundsUvwMin1;
                _boundsUvwMin[i32] = LightVolumeInstances[enabledId].BoundsUvwMin2;

                _boundsUvwMax[i3] = LightVolumeInstances[enabledId].BoundsUvwMax0;
                _boundsUvwMax[i31] = LightVolumeInstances[enabledId].BoundsUvwMax1;
                _boundsUvwMax[i32] = LightVolumeInstances[enabledId].BoundsUvwMax2;

            }

        }

        private void Start() {
            UpdateVolumes();
        }

        public void UpdateVolumes() {

            TryInitialize();
            UpdateDynamicVolumes();

            // Setting if volumes are enables
            if (LightVolumeAtlas == null || _enabledCount == 0) {
                VRCShader.SetGlobalFloat(_enabledID, 0);
                DataCRT.updateMode = CustomRenderTextureUpdateMode.OnDemand;
                MatrixCRT.updateMode = CustomRenderTextureUpdateMode.OnDemand;
                SmoothingCRT.updateMode = CustomRenderTextureUpdateMode.OnDemand;
                return;
            } else {
                VRCShader.SetGlobalFloat(_enabledID, 1);
                DataCRT.updateMode = CustomRenderTextureUpdateMode.Realtime;
                MatrixCRT.updateMode = CustomRenderTextureUpdateMode.Realtime;
                SmoothingCRT.updateMode = CustomRenderTextureUpdateMode.Realtime;
            }

            // Materials
            Material dataMaterial = DataCRT.material;
            Material matrixMaterial = MatrixCRT.material;
            Material smoothingMaterial = SmoothingCRT.material;

            // Setting CRT Material Values
            dataMaterial.SetVectorArray(_uvwMinID, _boundsUvwMin);
            dataMaterial.SetVectorArray(_uvwMaxID, _boundsUvwMax);
            dataMaterial.SetFloatArray(_isRotatedID, _isRotated);
            dataMaterial.SetVectorArray(_relativeRotationID, _relativeRotations);
            dataMaterial.SetVectorArray(_colorID, _colors);
            matrixMaterial.SetMatrixArray(_invWorldMatrixID, _invWorldMatrix);
            smoothingMaterial.SetVectorArray(_invLocalEdgeSmoothID, _invLocalEdgeSmooth);

            // Update CRTs
            DataCRT.Update();
            MatrixCRT.Update();
            SmoothingCRT.Update();

            // Global Single Variables
            VRCShader.SetGlobalFloat(_lightProbesBlendingID, LightProbesBlending ? 1 : 0);
            VRCShader.SetGlobalFloat(_sharpBoundsID, SharpBounds ? 1 : 0);
            VRCShader.SetGlobalFloat(_enabledCountID, _enabledCount);
            VRCShader.SetGlobalFloat(_additiveCountID, _additiveCount);
            VRCShader.SetGlobalFloat(_additiveMaxOverdrawID, Mathf.Min(Mathf.Max(AdditiveMaxOverdraw, 0), _additiveCount));

            // Global Textures
            VRCShader.SetGlobalTexture(_lightVolumeAtlasID, LightVolumeAtlas);
            VRCShader.SetGlobalTexture(_lightVolumeDataID, DataCRT);
            VRCShader.SetGlobalTexture(_lightVolumeSmoothingID, SmoothingCRT);
            VRCShader.SetGlobalTexture(_lightVolumeMatrixID, MatrixCRT);

        }
    }
}