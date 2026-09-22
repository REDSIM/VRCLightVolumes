#if !UDONSHARP && COMPILER_UDONSHARP
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
        [Tooltip("Enable for a volume that moves in game. Also enable Auto Update Volumes on the Manager.")]
        public bool IsDynamic = false;
        [Tooltip("Adds this baked lighting on top of other lighting. Use it for lights that move or switch together.")]
        public bool IsAdditive = false;
        [Tooltip("Tints the baked lighting. White keeps its original colors.")]
        [ColorUsage(showAlpha: false)] public Color Color = Color.white;
        [Tooltip("Scales the baked brightness. Set to 0 to turn it off.")]
        public float Intensity = 1f;
        [Tooltip("Width of the blend at the edges, in meters. Increase it for a softer transition between volumes.")]
        [Range(0, 1)] public float SmoothBlending = 0.25f;
        [Tooltip("Blend data updated by SetSmoothBlending() and UpdateTransform().")]
        public Vector4 InvLocalEdgeSmoothing = new Vector4();

        [Header("Baked Data")]
        [Tooltip("Baked lighting channel 0. Filled after a bake and packed into the Manager atlas.")]
        public Texture3D Texture0;
        [Tooltip("Baked lighting channel 1. Filled after a bake and packed into the Manager atlas.")]
        public Texture3D Texture1;
        [Tooltip("Baked lighting channel 2. Filled after a bake and packed into the Manager atlas.")]
        public Texture3D Texture2;
        [Tooltip("Bakery helper used during a bake. Removed from the build scene.")]
        [HideInInspector] public Component BakeryVolume;

        [Header("Color Correction")]
        [Tooltip("Adjusts the brightness of all baked lighting without rebaking.")]
        public float Exposure = 0f;
        [Tooltip("Adjusts dark parts of the baked lighting without rebaking.")]
        [Range(-1, 1)] public float Shadows = 0f;
        [Tooltip("Adjusts bright parts of the baked lighting without rebaking.")]
        [Range(-1, 1)] public float Highlights = 0f;

        [Header("Baking Setup")]
        [Tooltip("Include this volume in the next bake. Turn off to keep its current baked lighting.")]
        public bool Bake = true;
        [Tooltip("Reserves a white region in the atlas for custom lighting. Skips the lighting bake for this volume.")]
        public bool ReserveUVSpace = false;
        [Tooltip("Sets the grid size from the bounds and Voxels Per Unit.")]
        public bool AdaptiveResolution = true;
        [Tooltip("Higher values capture smaller lighting details but take longer to bake. Increase this only where you need more detail.")]
        public float VoxelsPerUnit = 3f;
        [Tooltip("Number of lighting samples along each axis. Used when Adaptive Resolution is off.")]
        public Vector3Int Resolution = new Vector3Int(16, 16, 16);

        [Header("Atlas Data")]
        [Tooltip("Atlas position of Texture 0. W stores the X scale. Set when the atlas is packed.")]
        public Vector4 BoundsUvwMin0 = new Vector4();
        [Tooltip("Atlas position of Texture 1. W stores the Y scale. Set when the atlas is packed.")]
        public Vector4 BoundsUvwMin1 = new Vector4();
        [Tooltip("Atlas position of Texture 2. W stores the Z scale. Set when the atlas is packed.")]
        public Vector4 BoundsUvwMin2 = new Vector4();

        [Header("Transform Data")]
        [Tooltip("Inverse rotation at the time of the bake. Used to rotate the stored lighting with the volume.")]
        public Quaternion InvBakedRotation = Quaternion.identity;
        [Tooltip("Transforms world positions into the volume. Set by UpdateTransform().")]
        public Matrix4x4 InvWorldMatrix = Matrix4x4.identity;
        [Tooltip("First row of the rotation relative to the baked pose. Set by UpdateTransform().")]
        public Vector3 RelativeRotationRow0 = Vector3.zero;
        [Tooltip("Second row of the rotation relative to the baked pose. Set by UpdateTransform().")]
        public Vector3 RelativeRotationRow1 = Vector3.zero;
        [Tooltip("Whether the volume has rotated from its baked pose. Set by UpdateTransform().")]
        public bool IsRotated = false;

        [Header("Runtime State")]
        [Tooltip("The Manager that owns this volume. Assign it before registration and keep the same Manager afterwards.")]
        public LightVolumeManager LightVolumeManager;
        [Tooltip("Breaks ties between volumes with the same priority. Use SetWeight() to change priority.")]
        [HideInInspector] public int RegistryOrder = 2147483647;
        [Tooltip("Volume priority. Higher values take precedence when volumes overlap.")]
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
            if (!_isRegisteredWithManager) RegisterWithManager();
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
            bool runtimeEnabled = enabled && gameObject.activeInHierarchy;
            IsActive = runtimeEnabled && Intensity != 0 && Color != Color.black;
            if (LightVolumeManager == null || !runtimeEnabled) return;
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
            Vector4 invLocalEdgeSmoothing = scl / safeRadius;
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
            InvLocalEdgeSmoothing = lossyScale / safeSmoothing;
            Quaternion rot = transformRot * InvBakedRotation;
            IsRotated = Mathf.Abs(Quaternion.Dot(rot, Quaternion.identity)) < 0.999999f;
            Matrix4x4 rotationMatrix = Matrix4x4.Rotate(rot);
            RelativeRotationRow0 = rotationMatrix.GetRow(0);
            RelativeRotationRow1 = rotationMatrix.GetRow(1);
            NotifyManager(false);
        }

    }
}
