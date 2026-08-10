#if !UDONSHARP && (UNITY_EDITOR || COMPILER_UDONSHARP)
#define UDONSHARP
#endif

using UnityEngine;
#if UDONSHARP
using UdonSharp;
#endif

namespace VRCLightVolumes {
    [AddComponentMenu("VRC Light Volumes/Light Volume")]
    [DisallowMultipleComponent]
#if UDONSHARP
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LightVolumeInstance : UdonSharpBehaviour
#else
    public class LightVolumeInstance : MonoBehaviour
#endif
    {

        [Header("Volume Setup")]
        [Tooltip("Defines whether this volume can be moved at runtime. Disabling this option slightly improves performance. Don't forget to enable \"Auto Update Volumes\" in your Light Volumes Setup to get these dynamic updates!")]
        public bool IsDynamic = false;
        [Tooltip("Additive volumes apply their light on top of others as an overlay. Useful for movable and toggleable lights. They can also project light onto static lightmapped objects if the surface shader supports it.")]
        public bool IsAdditive = false;
        [Tooltip("Multiplies the volume’s color by this value.")]
        [ColorUsage(showAlpha: false)] public Color Color = Color.white;
        [Tooltip("Brightness of the volume.")]
        public float Intensity = 1f;
        [Tooltip("Size in meters of this Light Volume's overlapping regions for smooth blending with other volumes.")]
        [Range(0, 1)] public float SmoothBlending = 0.25f;
        [Tooltip("Inversed edge smoothing in 3D atlas space. Recalculates via SetSmoothBlending(float radius), UpdateTransform(), and dynamic auto-update.")]
        public Vector4 InvLocalEdgeSmoothing = new Vector4();

        [Header("Baked Data")]
        [Tooltip("Texture3D with baked SH data required for future atlas packing. It is removed from the build copy after the atlas is generated. (L0r, L0g, L0b, L1r.z)")]
        public Texture3D Texture0;
        [Tooltip("Texture3D with baked SH data required for future atlas packing. It is removed from the build copy after the atlas is generated. (L1r.x, L1g.x, L1b.x, L1g.z)")]
        public Texture3D Texture1;
        [Tooltip("Texture3D with baked SH data required for future atlas packing. It is removed from the build copy after the atlas is generated. (L1r.y, L1g.y, L1b.y, L1b.z)")]
        public Texture3D Texture2;
        [Tooltip("Editor-only Bakery helper reference. The build preprocessor clears it from the build scene.")]
        [HideInInspector] public Component BakeryVolume;

        [Header("Color Correction")]
        [Tooltip("Makes volume brighter or darker.")]
        public float Exposure = 0f;
        [Tooltip("Makes dark volume colors brighter or darker.")]
        [Range(-1, 1)] public float Shadows = 0f;
        [Tooltip("Makes bright volume colors brighter or darker.")]
        [Range(-1, 1)] public float Highlights = 0f;

        [Header("Baking Setup")]
        [Tooltip("Uncheck it if you don't want to rebake this volume's textures.")]
        public bool Bake = true;
        [Tooltip("Reserves atlas UV space for this volume without baking its lighting data. Reserved voxels are written as white L0 and zero L1.")]
        public bool ReserveUVSpace = false;
        [Tooltip("Automatically sets the resolution based on the Voxels Per Unit value.")]
        public bool AdaptiveResolution = true;
        [Tooltip("Number of voxels used per meter, linearly. This value increases the Light Volume file size cubically.")]
        public float VoxelsPerUnit = 3f;
        [Tooltip("Manual Light Volume resolution in voxel count.")]
        public Vector3Int Resolution = new Vector3Int(16, 16, 16);

        [Header("Atlas Data")]
        [Tooltip("Min bounds of Texture0 in 3D atlas space. W stores Scale X.)")]
        public Vector4 BoundsUvwMin0 = new Vector4();
        [Tooltip("Min bounds of Texture1 in 3D atlas space. W stores Scale Y.")]
        public Vector4 BoundsUvwMin1 = new Vector4();
        [Tooltip("Min bounds of Texture2 in 3D atlas space. W stores Scale Z.")]
        public Vector4 BoundsUvwMin2 = new Vector4();

        [Header("Transform Data")]
        [Tooltip("Inverse rotation of the pose the volume was baked in. Updated when baked data is imported or the atlas is generated; runtime transform updates use this stored bake pose.")]
        public Quaternion InvBakedRotation = Quaternion.identity;
        [Tooltip("Inverse TRS matrix that transforms world positions into this volume's unit cube. Updated by UpdateTransform() and dynamic auto-update.")]
        public Matrix4x4 InvWorldMatrix = Matrix4x4.identity;
        [Tooltip("Current volume rotation row 0 relative to its baked pose. Updated by UpdateTransform() and dynamic auto-update.")]
        public Vector3 RelativeRotationRow0 = Vector3.zero;
        [Tooltip("Current volume rotation row 1 relative to its baked pose. Updated by UpdateTransform() and dynamic auto-update.")]
        public Vector3 RelativeRotationRow1 = Vector3.zero;
        [Tooltip("True when the current pose is rotated relative to the baked pose. Updated by UpdateTransform() and dynamic auto-update; an unrotated volume is cheaper to sample.")]
        public bool IsRotated = false;

        [Header("Runtime State")]
        [Tooltip("Reference to the world's single Light Volume Manager. Assign it before registration and do not change it afterwards.")]
        public LightVolumeManager LightVolumeManager;
        [Tooltip("Internal stable manager registry tie-breaker used when this volume is enabled at runtime. Use SetWeight(float weight) to change priority.")]
        [HideInInspector] public int RegistryOrder = 2147483647;
        [Tooltip("Manager registry sort weight. Higher weights are uploaded to shaders first.")]
        [HideInInspector] public float RegistryWeight = 0f;
        [HideInInspector] public bool IsActive = true;

        private Color _old_Color = Color.white;
        private float _old_Intensity = 1f;
        private bool _isRegisteredWithManager = false;

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        // Editor-only views of existing runtime state; no backing fields are added.
        internal bool RegisteredWithManagerPreview => _isRegisteredWithManager;
#endif

#if UDONSHARP
        // Low level Udon hacks:
        // _old_(Name) variables are the old values of the variables.
        // _onVarChange_(Name) methods (events) are called when the variable changes.
        // Without Udon it should be checked in Update
        public void _onVarChange_IsDynamic() {
            NotifyManager(true);
        }
        // Rebuilds volume ordering when Udon changes additive mode.
        public void _onVarChange_IsAdditive() {
            NotifyManager(true);
        }
        // Uploads a new Udon color without rebuilding transform data.
        public void _onVarChange_Color() {
            if (_old_Color != Color) {
                _old_Color = Color;
                NotifyManagerColor();
            }
        }
        // Uploads a new Udon intensity without rebuilding transform data.
        public void _onVarChange_Intensity() {
            if (_old_Intensity != Intensity) {
                _old_Intensity = Intensity;
                NotifyManagerColor();
            }
        }
#endif

#if UDONSHARP || UNITY_EDITOR
        // Registers a newly spawned instance after its initially empty manager reference is assigned.
        public void _onVarChange_LightVolumeManager() {
            RegisterWithManager();
        }
#endif

#if !UDONSHARP
        // Standalone Unity fallback when UdonSharp is not installed.
        private void Update() {
            if (_old_Color != Color || _old_Intensity != Intensity) {
                _old_Color = Color;
                _old_Intensity = Intensity;
                NotifyManagerColor();
            }
        }
#endif

        // Sends this instance change to the manager when it is active.
        private void NotifyManager(bool rebuildFinalData) {
            bool runtimeEnabled = enabled && gameObject.activeInHierarchy;
            IsActive = runtimeEnabled && Intensity != 0 && Color != Color.black;
            if (!runtimeEnabled) return;
            RegisterWithManager();
            if (LightVolumeManager == null) return;
            LightVolumeManager.NotifyLightVolumeChanged(this, rebuildFinalData);
        }

        // Color and intensity are the only changed record fields, so the manager can avoid pulling and repacking the other regular-volume data across the Udon boundary.
        private void NotifyManagerColor() {
            bool runtimeEnabled = enabled && gameObject.activeInHierarchy;
            IsActive = runtimeEnabled && Intensity != 0 && Color != Color.black;
            if (!runtimeEnabled) return;
            if (!_isRegisteredWithManager) RegisterWithManager();
            if (LightVolumeManager == null) return;
            LightVolumeManager.NotifyLightVolumeColorChanged(this);
        }

        // Registers once with the world's single manager.
        private void RegisterWithManager() {
            if (_isRegisteredWithManager) return;
            IsActive = enabled && gameObject.activeInHierarchy && Intensity != 0 && Color != Color.black;
            if (LightVolumeManager == null || !gameObject.activeInHierarchy || !enabled) return;
            _isRegisteredWithManager = true;
            LightVolumeManager.InitializeLightVolume(this);
        }

#if !UDONSHARP
        // Resolves the standalone Manager fallback after OnEnable runs without an assigned Manager.
        private void Start() {
            if (LightVolumeManager == null) {
                LightVolumeManager = FindObjectOfType<LightVolumeManager>();
            }
            RegisterWithManager();
        }
#endif

        // Registers the volume when its component or GameObject becomes active.
        private void OnEnable() {
            RegisterWithManager();
        }

        // Marks the volume inactive and removes it from the Manager registry.
        private void OnDisable() {
            IsActive = false;
            _isRegisteredWithManager = false;
            if (LightVolumeManager != null) LightVolumeManager.DeinitializeLightVolume(this);
        }

        // Sets dynamic mode and rebuilds the manager volume list only when it changes
        public void SetDynamic(bool isDynamic) {
            if (IsDynamic == isDynamic) return;
            IsDynamic = isDynamic;
            NotifyManager(true);
        }

        // Sets additive mode and rebuilds the manager volume list only when it changes
        public void SetAdditive(bool isAdditive) {
            if (IsAdditive == isAdditive) return;
            IsAdditive = isAdditive;
            NotifyManager(true);
        }

        // Sets light source color
        public void SetColor(Color color) {
            if (Color == color) return;
            Color = color;
            _old_Color = color;
            NotifyManagerColor();
        }

        // Sets light source intensity
        public void SetIntensity(float intensity) {
            if (Intensity == intensity) return;
            Intensity = intensity;
            _old_Intensity = intensity;
            NotifyManagerColor();
        }

        // Sets color and intensity together and publishes one manager notification.
        public void SetColorAndIntensity(Color color, float intensity) {
            if (Color == color && Intensity == intensity) return;
            Color = color;
            Intensity = intensity;
            _old_Color = color;
            _old_Intensity = intensity;
            NotifyManagerColor();
        }

        // Sets runtime render priority without changing the manager's authoring order
        public void SetWeight(float weight) {
            if (RegistryWeight == weight) return;
            RegistryWeight = weight;
            if (_isRegisteredWithManager) IsActive = enabled && gameObject.activeInHierarchy && Intensity != 0 && Color != Color.black;
            else RegisterWithManager();
            if (LightVolumeManager != null) LightVolumeManager.ReorderLightVolume(this);
        }

        // Calculates and sets invLocalEdgeBlending
        public void SetSmoothBlending(float radius) {
            Vector3 scl = transform.lossyScale;
            float safeRadius = Mathf.Max(radius, 0.00001f);
            Vector4 invLocalEdgeSmoothing = new Vector4(scl.x / safeRadius, scl.y / safeRadius, scl.z / safeRadius, 0f);
            if (SmoothBlending == radius && InvLocalEdgeSmoothing == invLocalEdgeSmoothing) return;
            SmoothBlending = radius;
            InvLocalEdgeSmoothing = invLocalEdgeSmoothing;
            NotifyManager(false);
        }

        // Recalculates the inverse world matrix and Relative L1 rotation from one Transform matrix read.
        public void UpdateTransform() {
            Transform instanceTransform = transform;
            Matrix4x4 localToWorldMatrix = instanceTransform.localToWorldMatrix;
            Quaternion transformRot = localToWorldMatrix.rotation;
            InvWorldMatrix = localToWorldMatrix.inverse;
            Vector3 lossyScale = localToWorldMatrix.lossyScale;
            float safeSmoothing = Mathf.Max(SmoothBlending, 0.00001f);
            InvLocalEdgeSmoothing = new Vector4(lossyScale.x / safeSmoothing, lossyScale.y / safeSmoothing, lossyScale.z / safeSmoothing, 0f);
            Quaternion rot = transformRot * InvBakedRotation;
            IsRotated = Mathf.Abs(Quaternion.Dot(rot, Quaternion.identity)) < 0.999999f;
            Matrix4x4 rotationMatrix = Matrix4x4.Rotate(rot);
            RelativeRotationRow0 = rotationMatrix.GetRow(0);
            RelativeRotationRow1 = rotationMatrix.GetRow(1);
            NotifyManager(false);
        }

    }
}
