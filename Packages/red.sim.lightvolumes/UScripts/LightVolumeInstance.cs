using UnityEngine;
using UnityEngine.Serialization;
using VRC.SDKBase;
#if UDONSHARP
using UdonSharp;
#endif

namespace VRCLightVolumes {
#if UDONSHARP
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LightVolumeInstance : UdonSharpBehaviour
#else
    public class LightVolumeInstance : MonoBehaviour
#endif
    {

        [SerializeField]
        [FormerlySerializedAs("Color")]
        [FieldChangeCallback(nameof(Color))]
        [Tooltip("Changing the color is useful for animating Additive volumes. You can even control the R, G, B channels separately this way.")]
        [ColorUsage(showAlpha: false, hdr: true)]
        private Color _color = Color.white;
        [Tooltip("Defines whether this volume can be moved in runtime. Disabling this option slightly improves performance. You can even change it in runtime.")]
        public bool IsDynamic = false;
        [Tooltip("Additive volumes apply their light on top of others as an overlay. Useful for movable lights like flashlights, projectors, disco balls, etc. They can also project light onto static lightmapped objects if the surface shader supports it.")]
        public bool IsAdditive = false;
        [Tooltip("Inverse rotation of the pose the volume was baked in. Automatically recalculated for dynamic volumes with auto-update, or manually via the UpdateRotation() method.")]
        public Quaternion InvBakedRotation = Quaternion.identity;
        [Space]
        [Tooltip("Min bounds of Texture0 in 3D atlas space. W stores Scale X.)")]
        public Vector4 BoundsUvwMin0 = new Vector4();
        [Tooltip("Min bounds of Texture1 in 3D atlas space. W stores Scale Y.")]
        public Vector4 BoundsUvwMin1 = new Vector4();
        [Tooltip("Min bounds of Texture2 in 3D atlas space. W stores Scale Z.")]
        public Vector4 BoundsUvwMin2 = new Vector4();
        [Space]
        [Tooltip("Max bounds of Texture0 in 3D atlas space. (Legacy)")]
        public Vector4 BoundsUvwMax0 = new Vector4();
        [Tooltip("Max bounds of Texture1 in 3D atlas space. (Legacy)")]
        public Vector4 BoundsUvwMax1 = new Vector4();
        [Tooltip("Max bounds of Texture2 in 3D atlas space. (Legacy)")]
        public Vector4 BoundsUvwMax2 = new Vector4();
        [Space]
        [Tooltip("Inversed edge smoothing in 3D atlas space. Recalculates via SetSmoothBlending(float radius) method.")]
        public Vector4 InvLocalEdgeSmoothing = new Vector4();
        [Tooltip("Inversed TRS matrix of this volume that transforms it into the 1x1x1 cube. Recalculates via the UpdateRotation() method.")]
        public Matrix4x4 InvWorldMatrix = Matrix4x4.identity;
        [Tooltip("Current volume's rotation relative to the rotation it was baked with. Mandatory for dynamic volumes. Recalculates via the UpdateRotation() method.")]
        public Vector4 RelativeRotation = new Vector4(0,0,0,1);
        [Tooltip("Current volume's rotation matrix row 0 relative to the rotation it was baked with. Mandatory for dynamic volumes. Recalculates via the UpdateRotation() method. (Legacy)")]
        public Vector3 RelativeRotationRow0 = Vector3.zero;
        [Tooltip("Current volume's rotation matrix row 1 relative to the rotation it was baked with. Mandatory for dynamic volumes. Recalculates via the UpdateRotation() method. (Legacy)")]
        public Vector3 RelativeRotationRow1 = Vector3.zero;
        [Tooltip("True if there is any relative rotation. No relative rotation improves performance. Recalculated via the UpdateRotation() method.")]
        public bool IsRotated = false;
        [Tooltip("Reference to the LightVolumeManager that manages this volume. Used to notify the manager about changes in this volume.")]
        public LightVolumeManager UpdateNotifier;

        public Color Color {
            get => _color;
            set {
                if (_color == value) return; // No change
                _color = value;
#if COMPILER_UDONSHARP
                if (Utilities.IsValid(UpdateNotifier)) UpdateNotifier.RequestUpdateVolumes();
#endif
            }
        }

        private void OnEnable() {
#if UDONSHARP
            if (Utilities.IsValid(UpdateNotifier))
#else
            if (UpdateNotifier != null)
#endif
                UpdateNotifier.RequestUpdateVolumes();
        }

        private void OnDisable() {
#if UDONSHARP
            if (Utilities.IsValid(UpdateNotifier))
#else
            if (UpdateNotifier != null)
#endif
                UpdateNotifier.RequestUpdateVolumes();
        }

        // Calculates and sets invLocalEdgeBlending
        public void SetSmoothBlending(float radius) {
            Vector3 scl = transform.lossyScale;
            InvLocalEdgeSmoothing = scl / Mathf.Max(radius, 0.00001f);

#if COMPILER_UDONSHARP
            if (Utilities.IsValid(UpdateNotifier)) UpdateNotifier.RequestUpdateVolumes();
#endif
        }

        // Recalculates inv TRS matrix and Relative L1 rotation
        public void UpdateTransform() {
            Quaternion transformRot = transform.rotation;
            InvWorldMatrix = Matrix4x4.TRS(transform.position, transformRot, transform.lossyScale).inverse;
            Quaternion rot = transformRot * InvBakedRotation;
            IsRotated = Quaternion.Dot(rot, Quaternion.identity) < 0.999999f;

            Matrix4x4 m = Matrix4x4.Rotate(rot);

            Vector4 row0 = m.GetRow(0);
            row0.w = 0;
            RelativeRotationRow0 = row0;
            Vector4 row1 = m.GetRow(1);
            row1.w = 0;
            RelativeRotationRow1 = row1;

            RelativeRotation = new Vector4(rot.x, rot.y, rot.z, rot.w);
        }

    }
}