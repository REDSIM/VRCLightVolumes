using UnityEngine;
using UnityEngine.Serialization;
#if UDONSHARP
using UdonSharp;
#endif

namespace VRCLightVolumes {
    [DisallowMultipleComponent]
#if UDONSHARP
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LightVolumeInstance : UdonSharpBehaviour
#else
    public class LightVolumeInstance : MonoBehaviour
#endif
    {

        [Header("Volume Setup")]
        [Tooltip("Defines whether this volume can be moved in runtime. Disabling this option slightly improves performance. Don't forget to enable \"Auto Update Volumes\" in your Light Volumes Setup to have this dynamic updates!")]
        public bool IsDynamic = false;
        [Tooltip("Additive volumes apply their light on top of others as an overlay. Useful for movable and togglable lights. They can also project light onto static lightmapped objects if the surface shader supports it.")]
        public bool IsAdditive = false;
        [Tooltip("Multiplies the volume’s color by this value.")]
        [ColorUsage(showAlpha: false)] public Color Color = Color.white;
        [Tooltip("Brightness of the volume.")]
        public float Intensity = 1f;
        [Tooltip("Inversed edge smoothing in 3D atlas space. Recalculates via SetSmoothBlending(float radius) method.")]
        public Vector4 InvLocalEdgeSmoothing = new Vector4();

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
        [HideInInspector] public bool IsActive = true;

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        [SerializeField, HideInInspector, FormerlySerializedAs("RelativeRotation")] private Vector4 _legacyRelativeRotation = new Vector4(0, 0, 0, 1);
        [SerializeField, HideInInspector, FormerlySerializedAs("BoundsUvwMax0")] private Vector4 _legacyBoundsUvwMax0 = Vector4.zero;
        [SerializeField, HideInInspector, FormerlySerializedAs("BoundsUvwMax1")] private Vector4 _legacyBoundsUvwMax1 = Vector4.zero;
        [SerializeField, HideInInspector, FormerlySerializedAs("BoundsUvwMax2")] private Vector4 _legacyBoundsUvwMax2 = Vector4.zero;
        [SerializeField, HideInInspector] private bool _legacyVolumeDataMigrated = false;
        private bool _legacyVolumeDataMigrationNeedsDirty = false;
#endif

        private Color _old_Color = Color.white;
        private float _old_Intensity = 1f;
        private bool _isRegisteredWithManager = false;

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

#if !UDONSHARP || UNITY_EDITOR
        // To make it work when changing values on UdonSharpBehaviour in editor
        private void Update() {
            if (_old_Color != Color || _old_Intensity != Intensity) {
                _old_Color = Color;
                _old_Intensity = Intensity;
                NotifyManager(false);
            }
        }
#endif

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        // Runs legacy serialized data migration when Unity reloads or validates this component in editor.
        private void OnValidate() {
            MigrateLegacyVolumeData();
        }

        // Converts serialized 2.x fallback fields into the 3.x compact volume layout when needed.
        public bool MigrateLegacyVolumeData() {
            if (_legacyVolumeDataMigrated) return false;
            bool changed = false;

            if (RelativeRotationRow0 == Vector3.zero && RelativeRotationRow1 == Vector3.zero && _legacyRelativeRotation != new Vector4(0, 0, 0, 1) && _legacyRelativeRotation != Vector4.zero) {
                Quaternion relativeRotation = new Quaternion(_legacyRelativeRotation.x, _legacyRelativeRotation.y, _legacyRelativeRotation.z, _legacyRelativeRotation.w);
                Matrix4x4 rotationMatrix = Matrix4x4.Rotate(relativeRotation);
                RelativeRotationRow0 = rotationMatrix.GetRow(0);
                RelativeRotationRow1 = rotationMatrix.GetRow(1);
                IsRotated = Quaternion.Dot(relativeRotation, Quaternion.identity) < 0.999999f;
                changed = true;
            }

            changed |= MigrateLegacyBoundsScale(ref BoundsUvwMin0, _legacyBoundsUvwMax0, 0);
            changed |= MigrateLegacyBoundsScale(ref BoundsUvwMin1, _legacyBoundsUvwMax1, 1);
            changed |= MigrateLegacyBoundsScale(ref BoundsUvwMin2, _legacyBoundsUvwMax2, 2);

            if (changed) _legacyVolumeDataMigrationNeedsDirty = true;
            _legacyVolumeDataMigrated = true;
            return changed;
        }

        // Returns and clears the delayed dirty request produced by OnValidate migration.
        public bool ConsumeLegacyVolumeDataMigrationDirty() {
            bool needsDirty = _legacyVolumeDataMigrationNeedsDirty;
            _legacyVolumeDataMigrationNeedsDirty = false;
            return needsDirty;
        }

        // Restores BoundsUvwMin.w scale from old explicit max vectors if a scene had only legacy bounds serialized.
        private static bool MigrateLegacyBoundsScale(ref Vector4 uvwMin, Vector4 legacyMax, int axis) {
            if (uvwMin.w != 0f || legacyMax == Vector4.zero) return false;
            float min = axis == 0 ? uvwMin.x : axis == 1 ? uvwMin.y : uvwMin.z;
            float max = axis == 0 ? legacyMax.x : axis == 1 ? legacyMax.y : legacyMax.z;
            uvwMin.w = max - min;
            return true;
        }
#endif

        // Sends this instance change to the manager when it is active.
        private void NotifyManager(bool rebuildFinalData) {
            IsActive = gameObject.activeInHierarchy && Intensity != 0 && Color != Color.black;
            if (LightVolumeManager == null || !gameObject.activeInHierarchy) return;
            LightVolumeManager.NotifyLightVolumeChanged(this, rebuildFinalData);
        }

        // Registers this instance once for the current active lifecycle.
        private void RegisterWithManager() {
            IsActive = gameObject.activeInHierarchy && Intensity != 0 && Color != Color.black;
            if (LightVolumeManager == null || _isRegisteredWithManager) return;
            LightVolumeManager.InitializeLightVolume(this);
            _isRegisteredWithManager = true;
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
            if (LightVolumeManager != null) {
                LightVolumeManager.DeinitializeLightVolume(this);
            }
            _isRegisteredWithManager = false;
        }

        // Calculates and sets invLocalEdgeBlending
        public void SetSmoothBlending(float radius) {
            Vector3 scl = transform.lossyScale;
            InvLocalEdgeSmoothing = scl / Mathf.Max(radius, 0.00001f);
            NotifyManager(true);
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
