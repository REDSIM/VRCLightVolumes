using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VRCLightVolumes {

    [ExecuteAlways]
    public class PointLightVolume : MonoBehaviour {
        
        public bool Dynamic = false;
        public LightType Type = LightType.PointLight;
        [Min(0.0001f)] public float Range = 5f;
        [ColorUsage(showAlpha: false)] public Color Color = Color.white;
        public float Intensity = 1f;
        public LightShape Shape = LightShape.Parametric;
        [Range(0.1f, 360)] public float Angle = 60f;
        [Range(0.001f, 1)] public float Falloff = 1f;
        [Min(0.001f)] public float AreaLightWidth = 1f;
        [Min(0.001f)] public float AreaLightHeight = 1f;
        public Texture2D FalloffLUT = null;
        public Texture2D Cookie = null;
        public Cubemap Cubemap = null;

        public int CustomID = -1;

        public PointLightVolumeInstance PointLightVolumeInstance;
        public LightVolumeSetup LightVolumeSetup;

        private Texture2D _falloffLUTPrev = null;
        private Texture2D _cookiePrev = null;
        private Cubemap _cubemapPrev = null;
        private LightShape _shapePrev = LightShape.Parametric;

        // Looks for LightVolumeSetup and LightVolumeInstance udon script and setups them if needed
        public void SetupDependencies() {
            if (PointLightVolumeInstance == null && !TryGetComponent(out PointLightVolumeInstance)) {
                PointLightVolumeInstance = gameObject.AddComponent<PointLightVolumeInstance>();
            }
            if (LightVolumeSetup == null) {
                LightVolumeSetup = FindObjectOfType<LightVolumeSetup>();
                if (LightVolumeSetup == null) {
                    var go = new GameObject("Light Volume Manager");
                    LightVolumeSetup = go.AddComponent<LightVolumeSetup>();
                    LightVolumeSetup.SyncUdonScript();
                }
            }
        }

        // Returns currently used custom texture depending on the light parameters
        public Texture GetCustomTexture() {
            if (Shape == LightShape.Parametric) {
                return null;
            } else if (Type == LightType.PointLight) {
                if(Shape == LightShape.LUT) {
                    return FalloffLUT;
                } else if (Shape == LightShape.Custom) {
                    return Cubemap;
                }
            } else if (Type == LightType.SpotLight) {
                if (Shape == LightShape.LUT) {
                    return FalloffLUT;
                } else if (Shape == LightShape.Custom) {
                    return Cookie;
                }
            }
            return null;
        }

        private void Update() {
            SetupDependencies();
#if UNITY_EDITOR
            if (_falloffLUTPrev != FalloffLUT || Cookie != _cookiePrev || _cubemapPrev != Cubemap || _shapePrev != Shape) {
                _falloffLUTPrev = FalloffLUT;
                _cookiePrev = Cookie;
                _cubemapPrev = Cubemap;
                _shapePrev = Shape;
                LightVolumeSetup.GenerateCustomTexturesArray();
            }
#endif
        }

        public void SyncUdonScript() {
            SetupDependencies();
            PointLightVolumeInstance.IsDynamic = Dynamic;
            PointLightVolumeInstance.SetColor(Color, Intensity);

            if(Type == LightType.PointLight) { // Point light
                PointLightVolumeInstance.SetRange(Range);
                if (Shape == LightShape.Custom && Cubemap != null) {
                    PointLightVolumeInstance.SetCustomTexture(CustomID); // Use Custom Cubemap Texture
                } else if (Shape == LightShape.LUT && FalloffLUT != null) {
                    PointLightVolumeInstance.SetLut(CustomID); // Use LUT
                } else {
                    PointLightVolumeInstance.SetParametric(); // Use this light in parametric mode
                }
                PointLightVolumeInstance.SetPointLight(); // Use it as Point Light
            } else if (Type == LightType.SpotLight) { // Spot Light
                PointLightVolumeInstance.SetRange(Range);
                if (Shape == LightShape.Custom && FalloffLUT != null) {
                    PointLightVolumeInstance.SetCustomTexture(CustomID); // Use Custom Projection Texture
                } else if (Shape == LightShape.LUT && FalloffLUT != null) {
                    PointLightVolumeInstance.SetLut(CustomID); // Use LUT
                } else {
                    PointLightVolumeInstance.SetParametric(); // Use this light in parametric mode
                }
                PointLightVolumeInstance.SetSpotLight(Angle, Falloff); // Don't use custom tex
            } else if (Type == LightType.AreaLight) { // Area light
                PointLightVolumeInstance.SetAreaLight(AreaLightWidth, AreaLightHeight);
            }

            LVUtils.MarkDirty(PointLightVolumeInstance);
        }

        private void Reset() {
            SetupDependencies();
            SyncUdonScript();
            LightVolumeSetup.RefreshVolumesList();
            LightVolumeSetup.SyncUdonScript();
        }

        private void OnEnable() {
            SetupDependencies();
            LightVolumeSetup.RefreshVolumesList();
            LightVolumeSetup.SyncUdonScript();
        }

        private void OnDisable() {
            if (LightVolumeSetup != null) {
                LightVolumeSetup.RefreshVolumesList();
                LightVolumeSetup.SyncUdonScript();
            }
        }

        private void OnDestroy() {
            if (LightVolumeSetup != null) {
                FalloffLUT = null;
                Cookie = null;
                Cubemap = null;
                LightVolumeSetup.GenerateCustomTexturesArray();
                LightVolumeSetup.RefreshVolumesList();
                LightVolumeSetup.SyncUdonScript();
            }
        }

        private void OnValidate() {
            SyncUdonScript();
        }

        // Delete self in play mode
        private void Start() {
            if (Application.isPlaying) Destroy(this);
        }

        public enum LightShape {
            Parametric,
            LUT,
            Custom
        }

        public enum LightType {
            PointLight,
            SpotLight,
            AreaLight,
        }

    }

}