using UnityEngine;
#if UDONSHARP
using UdonSharp;
#endif

namespace VRCLightVolumes {
    [AddComponentMenu("VRC Light Volumes/Light Volume (U# Script)")]
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
        [Tooltip("Inversed edge smoothing in 3D atlas space. Recalculates via SetSmoothBlending(float radius) method.")]
        public Vector4 InvLocalEdgeSmoothing = new Vector4();

        [Header("Baked Data")]
        [Tooltip("Texture3D with baked SH data required for future atlas packing. It is removed from the build copy after the atlas is generated. (L0r, L0g, L0b, L1r.z)")]
        public Texture3D Texture0;
        [Tooltip("Texture3D with baked SH data required for future atlas packing. It is removed from the build copy after the atlas is generated. (L1r.x, L1g.x, L1b.x, L1g.z)")]
        public Texture3D Texture1;
        [Tooltip("Texture3D with baked SH data required for future atlas packing. It is removed from the build copy after the atlas is generated. (L1r.y, L1g.y, L1b.y, L1b.z)")]
        public Texture3D Texture2;
#if BAKERY_INCLUDED
        [Tooltip("Editor-only Bakery helper reference. The build preprocessor clears it from the build scene.")]
        [HideInInspector] public Component BakeryVolume;
#endif

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
        [Tooltip("Inverse rotation of the pose the volume was baked in. Automatically recalculated for dynamic volumes with auto-update, or manually via the UpdateRotation() method.")]
        public Quaternion InvBakedRotation = Quaternion.identity;
        [Tooltip("Inversed TRS matrix of this volume that transforms it into the 1x1x1 cube. Recalculates via the UpdateRotation() method.")]
        public Matrix4x4 InvWorldMatrix = Matrix4x4.identity;
        [Tooltip("Current volume's rotation matrix row 0 relative to the rotation it was baked with. Mandatory for dynamic volumes. Recalculates via the UpdateRotation() method.")]
        public Vector3 RelativeRotationRow0 = Vector3.zero;
        [Tooltip("Current volume's rotation matrix row 1 relative to the rotation it was baked with. Mandatory for dynamic volumes. Recalculates via the UpdateRotation() method.")]
        public Vector3 RelativeRotationRow1 = Vector3.zero;
        [Tooltip("True if there is any relative rotation. No relative rotation improves performance. Recalculated via the UpdateRotation() method.")]
        public bool IsRotated = false;

        [Header("Runtime State")]
        [Tooltip("Reference to the Light Volume Manager. Needed for runtime initialization.")]
        public LightVolumeManager LightVolumeManager;
        [Tooltip("Internal stable manager registry tie-breaker used when this volume is enabled at runtime. Use SetWeight(float weight) to change priority.")]
        [HideInInspector] public int RegistryOrder = 2147483647;
        [Tooltip("Manager registry sort weight. Higher weights are uploaded to shaders first.")]
        [HideInInspector] public float RegistryWeight = 0f;
        [HideInInspector] public bool IsActive = true;

        private Color _old_Color = Color.white;
        private float _old_Intensity = 1f;
        private bool _isRegisteredWithManager = false;
        private LightVolumeManager _registeredManager;

#if UDONSHARP
        // Low level Udon hacks:
        // _old_(Name) variables are the old values of the variables.
        // _onVarChange_(Name) methods (events) are called when the variable changes.
        // Without Udon it should be checked in Update
        public void _onVarChange_Color() {
            if (_old_Color != Color) {
                _old_Color = Color;
                NotifyManager(false);
            }
        }
        public void _onVarChange_Intensity() {
            if (_old_Intensity != Intensity) {
                _old_Intensity = Intensity;
                NotifyManager(false);
            }
        }
#endif

#if UDONSHARP || UNITY_EDITOR
        // Registers this instance after the manager reference is assigned at runtime.
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
                NotifyManager(false);
            }
        }
#endif

        // Sends this instance change to the manager when it is active.
        private void NotifyManager(bool rebuildFinalData) {
            IsActive = gameObject.activeInHierarchy && Intensity != 0 && Color != Color.black;
            if (!gameObject.activeInHierarchy) return;
            RegisterWithManager();
            if (_registeredManager == null) return;
            _registeredManager.NotifyLightVolumeChanged(this, rebuildFinalData);
        }

        // Keeps the actual registry owner in sync when the public manager reference changes.
        private void RegisterWithManager() {
            IsActive = gameObject.activeInHierarchy && Intensity != 0 && Color != Color.black;
            LightVolumeManager targetManager = LightVolumeManager;
            if (_isRegisteredWithManager && _registeredManager != targetManager) UnregisterFromManager();
            if (targetManager == null || !gameObject.activeInHierarchy || !enabled) return;
            if (_isRegisteredWithManager && _registeredManager == targetManager) return;
            _registeredManager = targetManager;
            _isRegisteredWithManager = true;
            targetManager.InitializeLightVolume(this);
        }

        // Removes this instance from the manager that actually owns its registry slot.
        private void UnregisterFromManager() {
            // The private owner is runtime-only and can be empty after deserialization or when
            // an editor/runtime integration registered this volume through the manager API.
            LightVolumeManager registeredManager = _registeredManager != null ? _registeredManager : LightVolumeManager;
            _registeredManager = null;
            _isRegisteredWithManager = false;
            if (registeredManager != null) registeredManager.DeinitializeLightVolume(this);
        }

        private void Start() {
#if !UDONSHARP
            if (LightVolumeManager == null) {
                LightVolumeManager = FindObjectOfType<LightVolumeManager>();
            }
#endif
            RegisterWithManager();
        }

        private void OnEnable() {
            RegisterWithManager();
        }

        private void OnDisable() {
            IsActive = false;
            UnregisterFromManager();
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
            NotifyManager(false);
        }

        // Sets light source intensity
        public void SetIntensity(float intensity) {
            if (Intensity == intensity) return;
            Intensity = intensity;
            _old_Intensity = intensity;
            NotifyManager(false);
        }

        // Sets runtime registry weight and reorders this volume in the manager registry
        public void SetWeight(float weight) {
            if (RegistryWeight == weight) return;
            RegistryWeight = weight;
            RegisterWithManager();
            if (_registeredManager != null) _registeredManager.ReorderLightVolume(this);
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

        // Recalculates inv TRS matrix and Relative L1 rotation
        public void UpdateTransform() {
            Quaternion transformRot = transform.rotation;
            InvWorldMatrix = Matrix4x4.TRS(transform.position, transformRot, transform.lossyScale).inverse;
            Quaternion rot = transformRot * InvBakedRotation;
            IsRotated = Quaternion.Dot(rot, Quaternion.identity) < 0.999999f;

            Matrix4x4 m = Matrix4x4.Rotate(rot);
            RelativeRotationRow0 = m.GetRow(0);
            RelativeRotationRow1 = m.GetRow(1);
            NotifyManager(false);
        }

    }
}
