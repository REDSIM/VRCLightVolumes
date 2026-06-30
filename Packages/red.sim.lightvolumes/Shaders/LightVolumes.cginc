#ifndef VRC_LIGHT_VOLUMES_INCLUDED
#define VRC_LIGHT_VOLUMES_INCLUDED
#define VRCLV_VERSION 3
#define VRCLV_MIN_SUPPORTED_VERSION 2
#define VRCLV_MAX_VOLUMES_COUNT 32
#define VRCLV_MAX_LIGHTS_COUNT 128
#define VRCLV_MIN_SPECULAR_PERCEPTUAL_ROUGHNESS 0.089
#define VRCLV_MIN_N_DOT_V 0.0001


#ifndef SHADER_TARGET_SURFACE_ANALYSIS
cbuffer LightVolumeUniforms {
#endif

// Are Light Volumes enabled on scene? can be 0 or 1
uniform float _UdonLightVolumeEnabled;

// Returns 1, 2 or other number if there are light volumes on the scene. Number represents the light volumes system internal version number.
uniform float _UdonLightVolumeVersion;

// All volumes count in scene
uniform float _UdonLightVolumeCount;

// Additive volumes max overdraw count
uniform float _UdonLightVolumeAdditiveMaxOverdraw;

// Additive volumes count
uniform float _UdonLightVolumeAdditiveCount;

// Should volumes be blended with lightprobes?
uniform float _UdonLightVolumeProbesBlend;

// Should volumes be with sharp edges when not blending with each other
uniform float _UdonLightVolumeSharpBounds;

// World to Local (-0.5, 0.5) UVW Matrix 4x4
uniform float4x4 _UdonLightVolumeInvWorldMatrix[VRCLV_MAX_VOLUMES_COUNT];

// L1 SH matrix rotation relative to baked rotation. Stores row 0 and row 1; row 2 is reconstructed in shader.
uniform float4 _UdonLightVolumeRotation[VRCLV_MAX_VOLUMES_COUNT * 2];

// Value that is needed to smoothly blend volumes ( BoundsScale / edgeSmooth )
uniform float3 _UdonLightVolumeInvLocalEdgeSmooth[VRCLV_MAX_VOLUMES_COUNT];

// AABB bounds of islands on the 3D Texture atlas. XYZ: UvwMin, W: Scale per axis
uniform float4 _UdonLightVolumeUvwScale[VRCLV_MAX_VOLUMES_COUNT * 3];

// Legacy shader compatibility upload. Current cginc does not use volume occlusion data.
uniform float _UdonLightVolumeOcclusionCount;

// Color multiplier (RGB) | If we actually need to rotate L1 components at all (A)
uniform float4 _UdonLightVolumeColor[VRCLV_MAX_VOLUMES_COUNT];

// Point Lights count
uniform float _UdonPointLightVolumeCount;

// Cubemaps count in the custom textures array
uniform float _UdonPointLightVolumeCubeCount;

// Shadow cubemaps count in the shadow texture array
uniform float _UdonPointLightVolumeShadowCubeCount;

// Total shadow maps count in the shadow texture array
uniform float _UdonPointLightVolumeShadowCount;

// EVSM light bleed reduction amount. 0 disables reduction, 1 is strongest.
uniform float _UdonPointLightVolumeShadowBleedReduction;
// EVSM minimum variance clamp.
uniform float _UdonPointLightVolumeShadowMinVariance;

// For point light: XYZ = Position, W = Inverse squared range
// For spot light: XYZ = Position, W = Inverse squared range, negated
// For area light: XYZ = Position, W = Width
uniform float4 _UdonPointLightVolumePosition[VRCLV_MAX_LIGHTS_COUNT];

// For point light: XYZ = Color, W = Cos of angle (for LUT)
// For spot light: XYZ = Color, W = Cos of outer angle if no custom texture, tan of outer angle otherwise
// For area light: XYZ = Color, W = 2 + Height
uniform float4 _UdonPointLightVolumeColor[VRCLV_MAX_LIGHTS_COUNT];
// Shared extra point-light data. Area cookies use RGB = Color * Intensity. Custom spot cookies use X = width / height aspect. W = shadow near clip.
uniform float4 _UdonPointLightVolumeExtraData[VRCLV_MAX_LIGHTS_COUNT];

// For point light: XYZW = Rotation quaternion
// For spot light: XYZ = Direction, W = Cone falloff
// For area light: XYZW = Rotation quaternion
uniform float4 _UdonPointLightVolumeDirection[VRCLV_MAX_LIGHTS_COUNT];

// X = Custom ID:
//   If parametric: X stores 0
//   If uses custom lut: X stores LUT ID with positive sign
//   If uses custom texture: X stores texture ID with negative sign
// Y = shadow map ID when _UdonLightVolumeOcclusionCount is 0. Fraction stores inverted shading strength. Abs >= 10000 disables shading.
// Z = Squared Culling Range. Just a precalculated culling range to not recalculate it in shader.
// W = shadow far clip used to normalize shadow depth. Near clip is stored in _UdonPointLightVolumeExtraData.W. 0 for lights without shadows.
uniform float4 _UdonPointLightVolumeCustomID[VRCLV_MAX_LIGHTS_COUNT];

// For World Space Shadows:
//   XYZ = shadow bake position in world space.
//   W = shadow projection tangent half-angle for single texture shadows.
uniform float4 _UdonPointLightVolumeShadowReprojectionData[VRCLV_MAX_LIGHTS_COUNT];

//   XYZW = Rotation from current world space to baked shadow space.
uniform float4 _UdonPointLightVolumeShadowRotationData[VRCLV_MAX_LIGHTS_COUNT];

// If we are far enough from a light that the irradiance
// is guaranteed lower than the threshold defined by this value,
// we cull the light.
uniform float _UdonLightBrightnessCutoff;

#ifndef SHADER_TARGET_SURFACE_ANALYSIS
}
#endif

// Texel count and max mip for Area Light with cookie
uniform float _UdonPointLightVolumeTextureTexelCount;
uniform float _UdonPointLightVolumeTextureMaxMip;

#ifndef SHADER_TARGET_SURFACE_ANALYSIS

// Main 3D Texture atlas
uniform Texture3D _UdonLightVolume;
uniform SamplerState sampler_UdonLightVolume;
// First elements must be cubemap faces (6 face textures per cubemap). Then goes other textures
uniform Texture2DArray _UdonPointLightVolumeTexture;
uniform SamplerState sampler_UdonPointLightVolumeTexture;
// First elements are baked shadow cubemap faces, 6 face textures per cubemap.
uniform Texture2DArray _UdonPointLightVolumeShadowTexture;
uniform SamplerState sampler_UdonPointLightVolumeShadowTexture;
// Samples textures using mip 0. Shadow maps keep their own sampler state so they always use the shadow texture wrap mode.
#define LV_SAMPLE(tex, uvw) tex.SampleLevel(sampler_UdonLightVolume, uvw, 0)
#define LV_SAMPLE_POINT(uvw) _UdonPointLightVolumeTexture.SampleLevel(sampler_UdonPointLightVolumeTexture, uvw, 0)
#define LV_SAMPLE_POINT_LOD(uvw, lod) _UdonPointLightVolumeTexture.SampleLevel(sampler_UdonPointLightVolumeTexture, uvw, lod)
#define LV_SAMPLE_SHADOW(uvw) _UdonPointLightVolumeShadowTexture.SampleLevel(sampler_UdonPointLightVolumeShadowTexture, uvw, 0)

#else

// Dummy macro definition to satisfy MojoShader (surface shaders)
#define LV_SAMPLE(tex, uvw) float4(0,0,0,0)
#define LV_SAMPLE_POINT(uvw) float4(0,0,0,0)
#define LV_SAMPLE_POINT_LOD(uvw, lod) float4(0,0,0,0)
#define LV_SAMPLE_SHADOW(uvw) float4(0,0,0,0)

#endif

#define LV_PI 3.141592653589793f
#define LV_INV_PI 0.3183098861837907f
#define LV_PI2 6.283185307179586f
#define LV_EVSM_POSITIVE_EXPONENT 5.54f
#define LV_EVSM_NEGATIVE_EXPONENT 5.0f

// Smoothstep to 0, 1 but cheaper
float LV_Smoothstep01(float x) {
    return x * x * (3 - 2 * x);
}

// Approximate exp for EVSM range.
float LV_FastExp(float x) {
    x *= 0.25f;
    float y = 1.0f + x * (1.0f + x * (0.5f + x * (0.16666667f + x * (0.04166667f + x * (0.00833333f + x * 0.00138889f)))));
    y *= y;
    return y * y;
}

// Approximate log2 using frexp exponent extraction and a quadratic mantissa fit. Max error is below 0.008 log2 units.
float LV_FastLog2Positive(float x) {
    float exponent = 0;
    float mantissa = frexp(max(x, 1.0), exponent);
    float y = mantissa + mantissa - 1.0;
    return exponent - 1.0 + y * (1.3465554 - 0.3465554 * y);
}

// Rotates vector by Quaternion
float3 LV_MultiplyVectorByQuaternion(float3 v, float4 q) {
    float3 t = 2 * cross(q.xyz, v);
    return v + q.w * t + cross(q.xyz, t);
}

// Builds orthonormal axes from a normalized quaternion
void LV_QuaternionAxes(float4 q, out float3 xAxis, out float3 yAxis, out float3 zAxis) {
    float x2 = q.x + q.x, y2 = q.y + q.y, z2 = q.z + q.z;
    float xx = q.x * x2, yy = q.y * y2, zz = q.z * z2;
    float xy = q.x * y2, xz = q.x * z2, yz = q.y * z2;
    float wx = q.w * x2, wy = q.w * y2, wz = q.w * z2;
    xAxis = float3(1 - yy - zz, xy + wz, xz - wy);
    yAxis = float3(xy - wz, 1 - xx - zz, yz + wx);
    zAxis = float3(xz + wy, yz - wx, 1 - xx - yy);
}

// Rotates vector by Matrix 3x3 with precomputed third axis
float3 LV_MultiplyVectorByMatrix3x3(float3 v, float3 r0, float3 r1, float3 r2) {
    return float3(dot(v, r0), dot(v, r1), dot(v, r2));
}

// Fast approximate arctangent for positive values. Max error is small enough for area light attenuation
float LV_FastAtanPositive(float x) {
    if (x <= 1) { // atan small
        return x * rcp(1 + 0.280872 * x * x);
    } else { // atan large
        float invX = rcp(max(x, 1e-6));
        return LV_PI * 0.5 - invX * rcp(1 + 0.280872 * invX * invX);
    }
}

// Forms specular based on roughness
float LV_DistributionGGX(float NoH, float roughness) {
    float a2 = roughness * roughness;
    float f = (a2 - 1) * (NoH * NoH) + 1;
    return a2 * LV_INV_PI * rcp(f * f);
}

// Calculates fast correlated Smith visibility for GGX speculars.
float LV_VisibilitySmithGGXCorrelatedFast(float roughness, float NoV, float NoL) {
    return 0.5 * rcp(lerp(2.0 * NoL * NoV, NoL + NoV, roughness));
}

// Calculates Schlick Fresnel with f90 fixed to 1.
float3 LV_FresnelSchlick(float3 f0, float LoH) {
    float f = 1.0 - LoH;
    float f2 = f * f;
    f = f2 * f2 * f;
    return f + f0 * (1.0 - f);
}

// Normalizes a vector while avoiding undefined zero-length results.
float3 LV_NormalizeSafe(float3 v) {
    return v * rsqrt(max(dot(v, v), 1e-6));
}

// Checks if local UVW point is in bounds from -0.5 to +0.5
bool LV_PointLocalAABB(float3 localUVW) {
    return all(abs(localUVW) <= 0.5);
}

// Calculates local UVW using volume ID
float3 LV_LocalFromVolume(uint volumeID, float3 worldPos) {
    return mul(_UdonLightVolumeInvWorldMatrix[volumeID], float4(worldPos, 1)).xyz;
}

// Linear single SH L1 channel evaluation
float LV_EvaluateSH(float L0, float3 L1, float3 n) {
    return L0 + dot(L1, n);
}

// Projects a cubemap direction into face index and face UV. (xy: uv, z: face)
float3 LV_CubemapUvFace(float3 dir) {
    float2 uv;
    float face;
    float3 absDir = abs(dir);
    [branch] if (absDir.x >= absDir.y && absDir.x >= absDir.z) {
        face = dir.x > 0 ? 0 : 1;
        uv = float2((dir.x > 0 ? -dir.z : dir.z), -dir.y) * rcp(absDir.x);
    } else [branch] if (absDir.y >= absDir.z) {
        face = dir.y > 0 ? 2 : 3;
        uv = float2(dir.x, (dir.y > 0 ? dir.z : -dir.z)) * rcp(absDir.y);
    } else {
        face = dir.z > 0 ? 4 : 5;
        uv = float2((dir.z > 0 ? dir.x : -dir.x), -dir.y) * rcp(absDir.z);
    }
    return float3(uv * 0.5 + 0.5, face);
}

// Samples a cubemap from _UdonPointLightVolumeTexture array
float4 LV_SampleCubemapArray(uint id, float3 dir) {
    return LV_SAMPLE_POINT(LV_CubemapUvFace(dir) + float3(0, 0, id * 6));
}

// Samples a shadow map array face using face UV
float4 LV_SampleShadowMapArrayFace(uint id, uint face, float2 uv) {
    return LV_SAMPLE_SHADOW(float3(uv, id * 6 + face));
}

// Remaps visibility to suppress EVSM-style light bleeding.
float LV_ReduceLightBleeding(float visibility, float amount) {
    float edge = min(saturate(amount), 0.999f);
    float t = saturate((visibility - edge) * rcp(1.0f - edge));
    return LV_Smoothstep01(t);
}

// Applies exponential warp to normalized shadow depth.
float2 LV_EVSMWarpDepth(float depth) {
    depth = depth * 2.0f - 1.0f;
    return float2(LV_FastExp(LV_EVSM_POSITIVE_EXPONENT * depth), -LV_FastExp(-LV_EVSM_NEGATIVE_EXPONENT * depth));
}

// Evaluates the one-tailed Chebyshev upper bound used by EVSM.
float LV_EVSMChebyshevUpperBound(float2 moments, float mean, float minVariance, float bleedReduction) {
    float variance = max(moments.y - moments.x * moments.x, minVariance);
    float d = mean - moments.x;
    float pMax = variance * rcp(variance + d * d);
    pMax = LV_ReduceLightBleeding(pMax, bleedReduction);
    return mean <= moments.x ? 1.0f : pMax;
}

// Evaluates EVSM filtered visibility from sampled shadow moments.
float LV_ShadowEVSM(float4 moments, float distanceToShadowCenter, float nearClip, float farClip) {
    float normalizedDepth = saturate((distanceToShadowCenter - nearClip) * rcp(max(farClip - nearClip, 0.0001f)));
    float2 warpedDepth = LV_EVSMWarpDepth(normalizedDepth);
    float minVariance = max(_UdonPointLightVolumeShadowMinVariance, 0.0f);
    float bleedReduction = saturate(_UdonPointLightVolumeShadowBleedReduction);
    return min(LV_EVSMChebyshevUpperBound(moments.xz, warpedDepth.x, minVariance, bleedReduction), LV_EVSMChebyshevUpperBound(moments.yw, warpedDepth.y, minVariance, bleedReduction));
}

// Samples the per-light shadow map and returns attenuation
float LV_PointLightShadow(uint id, float3 worldPos, float3 lightDir, float sqDistanceToLight, float invDistanceToLight, float shadowNearClip, float shadowFarClip, float shadowIdData, uint shadowId) {
    uint shadowCubeCount = (uint)_UdonPointLightVolumeShadowCubeCount;
    float attenuation = 1;
    [branch] if (shadowId >= shadowCubeCount) { // Single slice shadows

        float4 shadowReprojectionData = _UdonPointLightVolumeShadowReprojectionData[id];
        float3 localDir = 0;
        float distanceToShadowCenter = 0;

        [branch] if (shadowIdData < 0) { // Local-space shadow
            distanceToShadowCenter = sqDistanceToLight * invDistanceToLight;
            localDir = LV_MultiplyVectorByQuaternion(-lightDir, _UdonPointLightVolumeShadowRotationData[id]);
        } else { // World-space shadow
            float3 bakeOffset = worldPos - shadowReprojectionData.xyz;
            float bakeSqLen = dot(bakeOffset, bakeOffset);
            [branch] if (bakeSqLen > 0.0001) { // Ignore degenerate vectors before normalizing baked direction
                float invBakeLen = rsqrt(bakeSqLen);
                distanceToShadowCenter = bakeSqLen * invBakeLen;
                localDir = LV_MultiplyVectorByQuaternion(bakeOffset * invBakeLen, _UdonPointLightVolumeShadowRotationData[id]);
            }
        }
        [branch] if (localDir.z > 0) { // Only sample receivers in front of the baked spot shadow camera
            float2 uv = localDir.xy * rcp(localDir.z * max(shadowReprojectionData.w, 0.0001f));
            [branch] if (max(abs(uv.x), abs(uv.y)) <= 1) { // Only sample inside the projected single shadow texture
                attenuation = LV_ShadowEVSM(LV_SAMPLE_SHADOW(float3(uv * 0.5 + 0.5, shadowId + shadowCubeCount * 5)), distanceToShadowCenter, shadowNearClip, shadowFarClip);
            }
        }

    } else { // Cubemap shadows
        float3 sampleDir = lightDir;
        float distanceToShadowCenter = sqDistanceToLight * invDistanceToLight;

        [branch] if (shadowIdData < 0) { // Local-space shadows
            sampleDir = LV_MultiplyVectorByQuaternion(lightDir, _UdonPointLightVolumeShadowRotationData[id]);
        } else { // World-space shadows
            float3 bakeDir = _UdonPointLightVolumeShadowReprojectionData[id].xyz - worldPos;
            float bakeSqLen = dot(bakeDir, bakeDir);
            [branch] if (bakeSqLen > 0.0001) { // Ignore degenerate vectors before normalizing baked cubemap direction
                float invBakeLen = rsqrt(bakeSqLen);
                sampleDir = LV_MultiplyVectorByQuaternion(bakeDir * invBakeLen, _UdonPointLightVolumeShadowRotationData[id]);
                distanceToShadowCenter = bakeSqLen * invBakeLen;
            }
        }
        float3 uvFace = LV_CubemapUvFace(sampleDir);
        attenuation = LV_ShadowEVSM(LV_SampleShadowMapArrayFace(shadowId, (uint)uvFace.z, uvFace.xy), distanceToShadowCenter, shadowNearClip, shadowFarClip);

    }
    return attenuation;
}

// Projects a front-facing quad light into L1 SH using a cheap solid-angle approximation.
// Caller must cull localPos.z <= 0 before calling.
float4 LV_ProjectFastQuadLightIrradianceSH(float3 lightToWorldPos, float3 localPos, float3 xAxis, float3 yAxis, float2 size, out float3 pointLightShadingDir) {
    float2 halfSize = size * 0.5;
    float area = max(size.x * size.y, 1e-6);
    float extentSq = max(dot(halfSize, halfSize), 1e-6);

    float2 closestXY = clamp(localPos.xy, -halfSize, halfSize);
    float2 rectDelta = localPos.xy - closestXY;
    float rectDeltaSq = dot(rectDelta, rectDelta);
    float planeRectSq = rectDeltaSq + localPos.z * localPos.z;
    float closestSqDist = max(planeRectSq, 1e-6);
    float centerSqDist = max(dot(localPos, localPos), 1e-6);

    float distanceBlend = planeRectSq * rcp(planeRectSq + extentSq);
    float solidSqDist = lerp(closestSqDist, centerSqDist, distanceBlend);
    float invSolidDist = rsqrt(solidSqDist);
    float invExtendedDist = rsqrt(solidSqDist + extentSq);

    float solidAngle = LV_FastAtanPositive(area * localPos.z * invSolidDist * invSolidDist * invExtendedDist * 0.25);
    float l0 = solidAngle * LV_INV_PI;

    float2 representativeXY = lerp(closestXY, 0, distanceBlend);
    float3 dir = xAxis * representativeXY.x + yAxis * representativeXY.y - lightToWorldPos;
    dir *= rsqrt(max(dot(dir, dir), 1e-6));
    pointLightShadingDir = dir;
    return float4(dir * (l0 * saturate(1 - l0)), l0);
}

// Calculates point light attenuation.
float3 LV_PointLightAttenuation(float sqdist, float sqlightSize, float3 color, float sqMaxDist) {
    float mask = saturate(1 - sqdist * rcp(sqMaxDist));
    return mask * mask * color * sqlightSize * rcp(sqdist + sqlightSize);
}

// Calculates point light solid angle coefficient
float LV_PointLightSolidAngle(float sqdist, float sqlightSize) {
    return saturate(sqrt(sqdist * rcp(sqlightSize + sqdist)));
}

// Calculates Point Light shading using a pre-scaled surface normal, precomputed bias, and finite source size.
float LV_PointLightShading(float3 pointLightShadingNormal, float pointLightShadingScale, float pointLightShadingBias, float3 lightDirNormal, float lightSpreadSq) {
    float lightSpread = saturate(sqrt(lightSpreadSq));
    float ramp = (dot(pointLightShadingNormal, lightDirNormal) + pointLightShadingScale * lightSpread) * rcp(1.0 + lightSpread) + pointLightShadingBias;
    return LV_Smoothstep01(saturate(ramp));
}

// Resolves spot cookie UV and culls fragments outside the projected cookie before expensive shadow work.
float2 LV_SphereSpotLightCookieUv(float3 lightDir, float4 lightRot, float tanAngle) {
    float3 localDir = LV_MultiplyVectorByQuaternion(-lightDir, lightRot);
    if (localDir.z <= 0) return 2; // Just to cull later
    else return localDir.xy * rcp(localDir.z * tanAngle);
}

// Samples a textured area emitter as a prefiltered mip-chain average: local high-frequency detail is blended with all available coarser mip levels to approximate the solid-angle tail from neighboring texels.
// Based on textured LTC prefiltering and filtered importance sampling:
// https://eheitzresearch.wordpress.com/415-2/
// https://developer.nvidia.com/gpugems/gpugems3/part-iii-rendering/chapter-20-gpu-based-importance-sampling
float4 LV_AreaLightCookie(float3 localPos, float2 size, uint textureId) {
    float2 safeSize = max(size, float2(0.0001, 0.0001));
    float2 halfSize = safeSize * 0.5;
    float2 closestXY = clamp(localPos.xy, -halfSize, halfSize);
    float2 rectDelta = localPos.xy - closestXY;
    float planeRectSq = dot(rectDelta, rectDelta) + localPos.z * localPos.z;
    float lightArea = max(safeSize.x * safeSize.y, 0.000001);
    float invLightArea = rcp(lightArea);
    float textureTexelCount = max(_UdonPointLightVolumeTextureTexelCount, 1.0);
    float filterArea = max(planeRectSq * LV_PI, lightArea * rcp(textureTexelCount));
    float texelsCovered = max(filterArea * textureTexelCount * invLightArea, 1.0);
    float shapeBlend = saturate(sqrt(filterArea * invLightArea));
    float2 representativeXY = closestXY * (1.0 - shapeBlend);

    float2 edgeUv = abs(representativeXY) * (2.0 * rcp(safeSize));
    float edgeBlend = LV_Smoothstep01(saturate((max(edgeUv.x, edgeUv.y) - 0.65) * 2.8571429));
    float grazingBlend = saturate(1 - abs(localPos.z) * rsqrt(max(dot(localPos, localPos), 0.000001)));
    float mip = 0.5 * LV_FastLog2Positive(texelsCovered) + edgeBlend * grazingBlend * (1.0 - shapeBlend) * 2.0;
    float maxMip = max(_UdonPointLightVolumeTextureMaxMip, 0.0);
    mip = min(mip, maxMip);

    float2 uv = representativeXY * rcp(safeSize) + 0.5;
    uv.x = 1.0 - uv.x;

    // Making 4 samples with different mip offsets and weights for the smoother result. All the values were tuned by hand.
    float mipRange = max(maxMip - mip, 0.0);
    float tap2Mip = mip + mipRange * 0.3;
    float tap3Mip = mip + mipRange * 0.5;
    float4 tap1Cookie = LV_SAMPLE_POINT_LOD(float3(uv, textureId), mip);
    float4 tap2Cookie = LV_SAMPLE_POINT_LOD(float3(uv, textureId), tap2Mip);
    float4 tap3Cookie = LV_SAMPLE_POINT_LOD(float3(uv, textureId), tap3Mip);
    float4 tap4Cookie = LV_SAMPLE_POINT_LOD(float3(uv, textureId), maxMip);
    float3 tap1Emission = tap1Cookie.rgb * tap1Cookie.a;
    float3 tap2Emission = tap2Cookie.rgb * tap2Cookie.a;
    float3 tap3Emission = tap3Cookie.rgb * tap3Cookie.a;
    float3 tap4Emission = tap4Cookie.rgb * tap4Cookie.a;
    float3 emission = tap1Emission + tap2Emission * 0.75 + tap3Emission * 0.55 + tap4Emission * 0.45;
    return float4(emission * 0.36363636, 1.0);
}

// Resolves point-light normal shading and baked shadows. lightDir samples shadows, normalMaskLightDir attenuates by surface normal.
// Returns true when the light remains visible enough to consume one overdraw slot.
bool LV_PointLightVolumeShadowMask(uint id, float shadowIdData, float shadowFarClip, float3 worldPos, float3 lightDir, float3 normalMaskLightDir, float distSq, float invDist, float sourceSpreadSq, float3 pointLightShadingNormal, float pointLightShadingScale, float pointLightShadingBias, out float shadow) {
    shadow = 1;
    bool shadowVisible = true;
    [branch] if (shadowIdData == 0) { // No baked shadow and full-strength normal shading
        [branch] if (pointLightShadingBias >= 0) { // Apply full-strength surface-normal shading when configured
            shadow = LV_PointLightShading(pointLightShadingNormal, pointLightShadingScale, pointLightShadingBias, normalMaskLightDir, sourceSpreadSq);
            shadowVisible = shadow > 0;
        }
    } else { // Optional normal shading strength and optional baked shadow map
        float shadowIdAbs = abs(shadowIdData);
        [branch] if (shadowIdAbs < 10000) { // Abs >= 10000 disables both normal shading and baked shadow sampling
            float shadingStrength = 1 - frac(shadowIdAbs);
            float normalAttenuation = 1;

            [branch] if (pointLightShadingBias >= 0) { // Apply surface-normal shading before strength blending
                normalAttenuation = LV_PointLightShading(pointLightShadingNormal, pointLightShadingScale, pointLightShadingBias, normalMaskLightDir, sourceSpreadSq);
                shadowVisible = normalAttenuation > 0 || shadingStrength < 1;
            }

            float shadowAttenuation = 1;
            [branch] if (shadowVisible && shadowIdAbs >= 1) { // Baked shadow
                uint shadowIndex = (uint)shadowIdAbs - 1; // Integer part stores shadow index + 1. Fraction stores inverted shading strength
                [branch] if (_UdonLightVolumeOcclusionCount == 0 && shadowIndex < (uint)_UdonPointLightVolumeShadowCount) { // Only sample runtime shadow texture array when legacy volume occlusion is not active
                    shadowAttenuation = LV_PointLightShadow(id, worldPos, lightDir, distSq, invDist, _UdonPointLightVolumeExtraData[id].w, shadowFarClip, shadowIdData, shadowIndex);
                }
            }
            shadow = lerp(1.0, saturate(normalAttenuation + shadowAttenuation - 1.0), shadingStrength); // Blend from fully lit to combined normal+shadow attenuation
        }
    }
    return shadowVisible;
}

// Samples one Point Light Volume. Returns true when this light consumes one overdraw slot
// Outputs: l0 = RGB irradiance, l1 = SH L1 direction term, lightDirNormal = center direction, specularSpreadSq = visible source size, shadow = normal mask and shadow
bool LV_PointLightVolumeContribution(uint id, float3 worldPos, float3 pointLightShadingNormal, float pointLightShadingScale, float pointLightShadingBias, out float3 l0, out float3 l1, out float3 lightDirNormal, out float specularSpreadSq, out float shadow) {

    l0 = 0; l1 = 0; lightDirNormal = 0; specularSpreadSq = 0; shadow = 1;
    bool counted = false;

    // IDs and range data
    float4 pos = _UdonPointLightVolumePosition[id]; // Light position and squared source size or range data
    float3 dir = pos.xyz - worldPos;
    float distSq = max(dot(dir, dir), 1e-6);
    float4 customID_data = _UdonPointLightVolumeCustomID[id];
    float rangeSq = customID_data.z; // Squared culling distance

    [branch] if (distSq <= rangeSq) { // In-range light. Out-of-range lights keep zero outputs and do not consume an overdraw slot
        int customId = (int) customID_data.x; // Custom Texture ID
        float4 color = _UdonPointLightVolumeColor[id]; // Color, angle

        [branch] if (pos.w >= 0 && color.w <= 1.5) { // Point light. Non-negative pos.w selects point-light sign, and color.w <= 1.5 excludes area lights
            float invDist = rsqrt(distSq);
            float3 lightDir = dir * invDist;
            float sourceSpreadSq = customId <= 0 ? pos.w * invDist * invDist : 0;

            // Point light is not fully culled by surface-normal shading or shadow visibility
            [branch] if (LV_PointLightVolumeShadowMask(id, customID_data.y, customID_data.w, worldPos, lightDir, lightDir, distSq, invDist, sourceSpreadSq, pointLightShadingNormal, pointLightShadingScale, pointLightShadingBias, shadow)) {
                counted = true;
                [branch] if (customId > 0) { // Point light with a baked attenuation LUT
                    float dirRadius = distSq * pos.w;
                    uint textureId = (uint) _UdonPointLightVolumeCubeCount * 5 + customId - 1;
                    float3 att = color.rgb * LV_SAMPLE_POINT(float3(0, sqrt(dirRadius), textureId)).xyz;
                    lightDirNormal = lightDir;
                    specularSpreadSq = 0;
                    l0 = att;
                    l1 = lightDir;
                } else { // Analytic point light, optionally tinted by a cubemap cookie
                    float invLightDistSq = rcp(distSq + pos.w);
                    float rangeMask = saturate(1 - distSq * rcp(rangeSq));
                    float3 att = color.rgb * (rangeMask * rangeMask * pos.w * invLightDistSq);
                    specularSpreadSq = sourceSpreadSq;
                    l1 = lightDir * saturate(sqrt(distSq * invLightDistSq));
                    lightDirNormal = lightDir;
                    [branch] if (customId < 0) { // Point light with cubemap cookie. Cubemap ID starts from zero and should not include single texture array slices count
                        l0 = att * LV_SampleCubemapArray((uint)(-customId - 1), LV_MultiplyVectorByQuaternion(lightDir, _UdonPointLightVolumeDirection[id])).xyz;
                    } else { // Plain analytic point light without custom texture data.
                        l0 = att;
                    }
                }
            }
        } else { // Non-point light. Split into spot lights and area lights
            [branch] if (pos.w < 0) { // Spot light. Negative pos.w selects spot-light sign, magnitude is source size or LUT inverse range

                float invDist = rsqrt(distSq);
                float3 lightDir = dir * invDist;
                float angle = color.w;
                float spotMask = 0;
                float spotConeFalloff = 0;
                float2 cookieUv = 0;
                bool spotVisible = true;

                [branch] if (customId >= 0) { // Parametric or LUT spot light. Direction vector and cone falloff are stored directly
                    float4 directionData = _UdonPointLightVolumeDirection[id]; // Dir + falloff
                    spotMask = dot(directionData.xyz, -lightDir) - angle;
                    spotVisible = spotMask >= 0;
                    spotConeFalloff = directionData.w;
                } else { // Textured spot light. Rotation projects the light direction into cookie UV space
                    float4 directionData = _UdonPointLightVolumeDirection[id]; // Rotation
                    cookieUv = LV_SphereSpotLightCookieUv(lightDir, directionData, angle);
                    cookieUv.y *= max(_UdonPointLightVolumeExtraData[id].x, 0.001);
                    spotVisible = all(abs(cookieUv) <= 1);
                }

                [branch] if (spotVisible) { // Spot receiver is inside the parametric cone or inside the projected cookie rectangle
                    float sourceSpreadSq = customId <= 0 ? -pos.w * invDist * invDist : 0;

                    // Spot light is not fully culled by surface-normal shading or shadow visibility
                    [branch] if (LV_PointLightVolumeShadowMask(id, customID_data.y, customID_data.w, worldPos, lightDir, lightDir, distSq, invDist, sourceSpreadSq, pointLightShadingNormal, pointLightShadingScale, pointLightShadingBias, shadow)) {
                        counted = true;

                        [branch] if (customId > 0) { // Spot light with Attenuation LUT. LUT already includes cone attenuation
                            float dirRadius = distSq * -pos.w;
                            float spot = 1 - saturate(spotMask * rcp(1 - angle));
                            uint textureId = (uint) _UdonPointLightVolumeCubeCount * 5 + customId - 1;
                            float3 att = color.rgb * LV_SAMPLE_POINT(float3(sqrt(float2(spot, dirRadius)), textureId)).xyz;
                            lightDirNormal = lightDir;
                            specularSpreadSq = 0;
                            l0 = att;
                            l1 = lightDir;
                        } else { // Analytic spot light, optionally multiplied by a projected cookie
                            float3 att = LV_PointLightAttenuation(distSq, -pos.w, color.rgb, rangeSq);
                            lightDirNormal = lightDir;
                            specularSpreadSq = sourceSpreadSq;
                            [branch] if (customId < 0) { // Textured spot light. Cookie RGB tints the light and alpha masks it
                                uint textureId = (uint) _UdonPointLightVolumeCubeCount * 5 - customId - 1;
                                float4 cookie = LV_SAMPLE_POINT(float3(cookieUv * 0.5 + 0.5, textureId));
                                l0 = att * cookie.rgb * cookie.a;
                                l1 = lightDir * LV_PointLightSolidAngle(distSq, -pos.w * (1 - saturate(rsqrt(1 + angle * angle))));
                            } else { // Plain analytic spot light. Cone falloff is evaluated procedurally
                                l0 = att * LV_Smoothstep01(saturate(spotMask * spotConeFalloff));
                                l1 = lightDir * LV_PointLightSolidAngle(distSq, -pos.w * saturate(1 - angle));
                            }
                        }
                    }
                }
            } else { // Area light. Positive pos.w stores width, and color.w stores 2 + height
                float3 areaNormal, areaXAxis, areaYAxis;
                float4 areaRotation = _UdonPointLightVolumeDirection[id]; // Rotation
                float3 lightToWorldPos = worldPos - pos.xyz;
                float2 areaSize = float2(pos.w, color.w - 2);
                LV_QuaternionAxes(areaRotation, areaXAxis, areaYAxis, areaNormal);
                float3 areaLocalPos = float3(dot(lightToWorldPos, areaXAxis), dot(lightToWorldPos, areaYAxis), dot(lightToWorldPos, areaNormal));

                [branch] if (areaLocalPos.z > 0) { // Receiver is in front of the area emitter plane
                    float3 areaPointLightShadingDir;
                    float sourceSpreadSq = dot(areaSize, areaSize) * (0.25 * rcp(distSq));
                    float4 areaLightSH = LV_ProjectFastQuadLightIrradianceSH(lightToWorldPos, areaLocalPos, areaXAxis, areaYAxis, areaSize, areaPointLightShadingDir);
                    float areaAttenuation = saturate((rangeSq - distSq) * rcp(rangeSq));

                    [branch] if (areaLightSH.w > 0 && areaAttenuation > 0) { // Area projection has non-zero solid angle and remains inside its culling range
                        float4 cookie = 1;
                        bool areaVisible = true;

                        [branch] if (customId < 0) { // Textured area light
                            uint textureId = (uint) _UdonPointLightVolumeCubeCount * 5 - customId - 1;
                            cookie = LV_AreaLightCookie(areaLocalPos, areaSize, textureId);
                            color.rgb = _UdonPointLightVolumeExtraData[id].rgb;
                            areaVisible = min(cookie.a, max(max(cookie.r, cookie.g), cookie.b)) > 0;
                        }

                        [branch] if (areaVisible) { // Area light is either untextured or has a non-black/non-transparent cookie sample
                            float invDist = rsqrt(distSq);
                            float3 lightDir = dir * invDist;

                            // Area light is not fully culled by surface-normal shading or shadow visibility
                            [branch] if (LV_PointLightVolumeShadowMask(id, customID_data.y, customID_data.w, worldPos, lightDir, areaPointLightShadingDir, distSq, invDist, sourceSpreadSq, pointLightShadingNormal, pointLightShadingScale, pointLightShadingBias, shadow)) {
                                counted = true;
                                lightDirNormal = lightDir;
                                specularSpreadSq = sourceSpreadSq;
                                l0 = color.rgb * (areaAttenuation * LV_PI * areaLightSH.w) * cookie.rgb * cookie.a;
                                l1 = areaLightSH.xyz * rcp(areaLightSH.w);
                            }
                        }
                    }
                }
            }
        }
    }
    return counted;
}

// Samples 3 SH textures and packing them into L1 channels
void LV_SampleLightVolumeTex(float3 uvw0, float3 uvw1, float3 uvw2, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b) {
    // Sampling 3D Atlas
    float4 tex0 = LV_SAMPLE(_UdonLightVolume, uvw0);
    float4 tex1 = LV_SAMPLE(_UdonLightVolume, uvw1);
    float4 tex2 = LV_SAMPLE(_UdonLightVolume, uvw2);
    // Packing final data
    L0 = tex0.rgb;
    L1r = float3(tex1.r, tex2.r, tex0.a);
    L1g = float3(tex1.g, tex2.g, tex1.a);
    L1b = float3(tex1.b, tex2.b, tex2.a);
}

// Bounds mask for a volume rotated in world space, using local UVW
float LV_BoundsMask(float3 localUVW, float3 invLocalEdgeSmooth) {
    float3 fade = saturate((0.5 - abs(localUVW)) * invLocalEdgeSmooth);
    return fade.x * fade.y * fade.z;
}

// Default light probes SH components
void LV_SampleLightProbe(inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b) {
    L0 += float3(unity_SHAr.w, unity_SHAg.w, unity_SHAb.w);
    L1r += unity_SHAr.xyz;
    L1g += unity_SHAg.xyz;
    L1b += unity_SHAb.xyz;
}

// Calculates atlas UVW coordinates for a volume sample using the compact bounds layout
void LV_VolumeAtlasUVW(uint id, float3 localUVW, out float3 uvw0, out float3 uvw1, out float3 uvw2) {
    uint uvwID = id * 3;
    float4 uvwPos0 = _UdonLightVolumeUvwScale[uvwID];
    float4 uvwPos1 = _UdonLightVolumeUvwScale[uvwID + 1];
    float4 uvwPos2 = _UdonLightVolumeUvwScale[uvwID + 2];
    float3 uvwScale = float3(uvwPos0.w, uvwPos1.w, uvwPos2.w);

    float3 uvwScaled = saturate(localUVW + 0.5) * uvwScale;
    uvw0 = uvwPos0.xyz + uvwScaled;
    uvw1 = uvwPos1.xyz + uvwScaled;
    uvw2 = uvwPos2.xyz + uvwScaled;
}

// Samples a Volume with ID and Local UVW
void LV_SampleVolume(uint id, float3 localUVW, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b) {

    // Additive UVW
    float3 uvw0, uvw1, uvw2;
    LV_VolumeAtlasUVW(id, localUVW, uvw0, uvw1, uvw2);

    // Sample additive
    float3 l0, l1r, l1g, l1b;
    LV_SampleLightVolumeTex(uvw0, uvw1, uvw2, l0, l1r, l1g, l1b);

    // Color correction
    float4 color = _UdonLightVolumeColor[id];
    L0 += l0 * color.rgb;
    l1r *= color.r;
    l1g *= color.g;
    l1b *= color.b;

    // Rotate if needed
    [branch] if (color.a != 0) {
        uint rotationID = id * 2;
        float3 r0 = _UdonLightVolumeRotation[rotationID].xyz;
        float3 r1 = _UdonLightVolumeRotation[rotationID + 1].xyz;
        float3 r2 = cross(r0, r1);
        L1r += LV_MultiplyVectorByMatrix3x3(l1r, r0, r1, r2);
        L1g += LV_MultiplyVectorByMatrix3x3(l1g, r0, r1, r2);
        L1b += LV_MultiplyVectorByMatrix3x3(l1b, r0, r1, r2);
    } else {
        L1r += l1r;
        L1g += l1g;
        L1b += l1b;
    }

}

// Calculates speculars for any directional data, with provided f0. Normal and view direction must be normalized.
float3 LV_Specular(float3 f0, float smoothness, float3 worldNormal, float3 viewDir, float3 l0, float3 l1) {
    float3 lightDirNormal = LV_NormalizeSafe(l1);
    float nh = saturate(dot(worldNormal, normalize(lightDirNormal + viewDir)));
    float roughness = 1 - smoothness * 0.9;
    return max(LV_DistributionGGX(nh, roughness * roughness) * l0 * f0, 0.0) * 1.5;
}

// Calculates physically based GGX speculars for one normalized light direction. Normal, view direction, and light direction must be normalized.
float3 LV_SpecularBRDFDirection(float3 f0, float roughness, float roughnessSq, float NoV, float3 worldNormal, float3 viewDir, float3 l0, float3 lightDirNormal, float lightSpreadSq) {
    float centerNoL = dot(worldNormal, lightDirNormal);
    float lightSpread = saturate(sqrt(lightSpreadSq));
    float NoL = saturate((centerNoL + lightSpread) * rcp(1.0 + lightSpread));
    float3 specular = float3(0.0, 0.0, 0.0);
    [branch] if (NoL > 0) {
        // For normalized L/V, derive half-vector terms from dot products without materializing H.
        float LoV = dot(lightDirNormal, viewDir);
        float centerNoV = dot(worldNormal, viewDir);
        float invHalfLen = rsqrt(max(2.0 + 2.0 * LoV, 1e-6));
        float NoH = saturate((centerNoL + centerNoV) * invHalfLen);
        float LoH = saturate((1.0 + LoV) * invHalfLen);
        float lightRoughnessSq = lightSpreadSq * rcp(max(4.0 * LoH * LoH, 0.0001));
        float effectiveRoughnessSq = (roughnessSq + lightRoughnessSq) * rcp(1.0 + lightRoughnessSq);
        float f = (effectiveRoughnessSq - 1) * (NoH * NoH) + 1;
        float D = effectiveRoughnessSq * rcp(f * f);
        float V = LV_VisibilitySmithGGXCorrelatedFast(roughness, NoV, NoL);
        float3 F = LV_FresnelSchlick(f0, LoH);
        specular = l0 * (D * V * NoL) * F;
    }
    return specular;
}

// Calculates speculars per-channel for light volumes or any SH L1 data with provided f0. Normal and view direction must be normalized.
float3 LV_LightVolumeSpecular(float3 f0, float smoothness, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b) {
    float roughness = 1 - smoothness * 0.9;
    float roughExp = roughness * roughness;
    float rSpec = LV_DistributionGGX(saturate(dot(worldNormal, normalize(LV_NormalizeSafe(L1r) + viewDir))), roughExp);
    float gSpec = LV_DistributionGGX(saturate(dot(worldNormal, normalize(LV_NormalizeSafe(L1g) + viewDir))), roughExp);
    float bSpec = LV_DistributionGGX(saturate(dot(worldNormal, normalize(LV_NormalizeSafe(L1b) + viewDir))), roughExp);
    float3 specs = (rSpec + gSpec + bSpec) * f0;
    float3 coloredSpecs = specs * max(float3(dot(reflect(-L1r, worldNormal), viewDir), dot(reflect(-L1g, worldNormal), viewDir), dot(reflect(-L1b, worldNormal), viewDir)), 0);
    return max(lerp(coloredSpecs + specs * L0, coloredSpecs * 3, smoothness) * 0.5, 0.0);
}

// Calculates L1 SH and individual speculars based on PBR parameters and custom f0. Only samples point lights, not volumes. Accumulates into L0/L1r/L1g/L1b/specular.
void LV_PointLightVolumeSHSpecular(float3 worldPos, float3 worldNormal, float3 specularViewDir, float smoothness, float3 f0, float pointLightShading, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b, inout float3 specular) {
    uint pointCount = min((uint) _UdonPointLightVolumeCount, VRCLV_MAX_LIGHTS_COUNT);
    [branch] if (pointCount == 0) return;

    float pointLightShadingScale = pointLightShading * 0.5; // Half-strength scale for the normal-shading ramp
    float3 pointLightShadingNormal = worldNormal * pointLightShadingScale; // Pre-scaled normal for point-light shading
    float pointLightShadingBias = pointLightShading > 0 ? 0.5 + 0.5 * saturate(1 - pointLightShading) : -1; // Bias for normal-shading ramp, -1 disables it
    float specularPerceptualRoughness = max(1.0 - smoothness, VRCLV_MIN_SPECULAR_PERCEPTUAL_ROUGHNESS); // Smoothness converted to clamped perceptual roughness
    float specularRoughness = specularPerceptualRoughness * specularPerceptualRoughness; // GGX roughness
    float specularRoughnessSq = specularRoughness * specularRoughness; // Squared GGX roughness for BRDF widening
    float specularNoV = max(dot(worldNormal, specularViewDir), VRCLV_MIN_N_DOT_V); // Clamped NdotV for specular visibility
    uint maxOverdraw = min((uint) _UdonLightVolumeAdditiveMaxOverdraw, VRCLV_MAX_LIGHTS_COUNT); // Max point lights to accumulate
    uint pcount = 0; // Accumulated point-light count

    [loop] for (uint pid = 0; pid < pointCount && pcount < maxOverdraw; pid++) {
        float3 l0, l1, lightDirNormal;
        float specularSpreadSq, shadow;
        if (LV_PointLightVolumeContribution(pid, worldPos, pointLightShadingNormal, pointLightShadingScale, pointLightShadingBias, l0, l1, lightDirNormal, specularSpreadSq, shadow)) {
            l0 *= shadow;
            [branch] if (max(max(l0.r, l0.g), l0.b) > 0) {
                L0 += l0;
                L1r += l1 * l0.r;
                L1g += l1 * l0.g;
                L1b += l1 * l0.b;
                specular += LV_SpecularBRDFDirection(f0, specularRoughness, specularRoughnessSq, specularNoV, worldNormal, specularViewDir, l0, lightDirNormal, specularSpreadSq);
            }
            pcount += 1;
        }
    }
}

// Calculates L1 SH based on the world position. Only samples point lights, not volumes. Accumulates into L0/L1r/L1g/L1b.
void LV_PointLightVolumeSH(float3 worldPos, float3 worldNormal, float pointLightShading, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b) {
    uint pointCount = min((uint) _UdonPointLightVolumeCount, VRCLV_MAX_LIGHTS_COUNT);
    [branch] if (pointCount == 0) return;

    float pointLightShadingScale = pointLightShading * 0.5; // Half-strength scale for the normal-shading ramp
    float3 pointLightShadingNormal = worldNormal * pointLightShadingScale; // Pre-scaled normal for point-light shading
    float pointLightShadingBias = pointLightShading > 0 ? 0.5 + 0.5 * saturate(1 - pointLightShading) : -1; // Bias for normal-shading ramp, -1 disables it
    uint maxOverdraw = min((uint) _UdonLightVolumeAdditiveMaxOverdraw, VRCLV_MAX_LIGHTS_COUNT); // Max point lights to accumulate
    uint pcount = 0; // Accumulated point-light count

    [loop] for (uint pid = 0; pid < pointCount && pcount < maxOverdraw; pid++) {
        float3 l0, l1, unused_lightDirNormal;
        float unused_specularSpreadSq, shadow;
        if (LV_PointLightVolumeContribution(pid, worldPos, pointLightShadingNormal, pointLightShadingScale, pointLightShadingBias, l0, l1, unused_lightDirNormal, unused_specularSpreadSq, shadow)) {
            l0 *= shadow;
            L0 += l0;
            L1r += l1 * l0.r;
            L1g += l1 * l0.g;
            L1b += l1 * l0.b;
            pcount += 1;
        }
    }

}

// Calculates L1 SH based on the world position from regular volumes only.
void LV_LightVolumeRegularSH(float3 worldPos, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b) {
    
    // Clamping global iteration counts
    uint volumesCount = min((uint) _UdonLightVolumeCount, VRCLV_MAX_VOLUMES_COUNT);
    [branch] if (volumesCount == 0) {
        LV_SampleLightProbe(L0, L1r, L1g, L1b);
        return;
    }

    uint additiveCount = min((uint) _UdonLightVolumeAdditiveCount, volumesCount);

    [branch] if (volumesCount <= additiveCount) {
        LV_SampleLightProbe(L0, L1r, L1g, L1b);
        return;
    }

    uint volumeID_A = -1; // Main, dominant volume ID
    uint volumeID_B = -1; // Secondary volume ID to blend main with

    float3 localUVW = 0; // Last local UVW to use in disabled Light Probes mode
    float3 localUVW_A = 0; // Main local UVW
    float3 localUVW_B = 0; // Secondary local UVW

    // Are A and B volumes NOT found?
    bool isNoA = true, isNoB = true;

    // Iterating through regular light volumes with simplified algorithm requiring Light Volumes to be sorted by weight in descending order
    [loop] for (uint id = additiveCount; id < volumesCount; id++) {
        localUVW = LV_LocalFromVolume(id, worldPos);
        [branch] if (LV_PointLocalAABB(localUVW)) { // Intersection test
            [branch] if (isNoA) { // First, searching for volume A
                volumeID_A = id;
                localUVW_A = localUVW;
                isNoA = false;
            } else { // Next, searching for volume B if A found
                volumeID_B = id;
                localUVW_B = localUVW;
                isNoB = false;
                break;
            }
        }
    }

    // If no volumes found, using Light Probes as fallback
    [branch] if (isNoA) {
        [branch] if (_UdonLightVolumeProbesBlend) {
            LV_SampleLightProbe(L0, L1r, L1g, L1b);
            return;
        }

        // Fallback to the lowest weight light volume if outside every volume
        LV_SampleVolume(volumesCount - 1, localUVW, L0, L1r, L1g, L1b);
        return;
    }

    // Volume A SH components and mask to blend volume sides
    float3 L0_A = 0, L1r_A = 0, L1g_A = 0, L1b_A = 0;

    // Sampling Light Volume A
    LV_SampleVolume(volumeID_A, localUVW_A, L0_A, L1r_A, L1g_A, L1b_A);

    float mask = LV_BoundsMask(localUVW_A, _UdonLightVolumeInvLocalEdgeSmooth[volumeID_A]);
    [branch] if (mask == 1) { // Returning SH A result if it's the center of mask

        L0  += L0_A;
        L1r += L1r_A;
        L1g += L1g_A;
        L1b += L1b_A;
        return;

    }

    float3 L0_B = 0, L1r_B = 0, L1g_B = 0, L1b_B = 0; // Volume B SH components

    [branch] if (isNoB) {
        [branch] if (_UdonLightVolumeSharpBounds) {
            L0  += L0_A;
            L1r += L1r_A;
            L1g += L1g_A;
            L1b += L1b_A;
            return;
        }

        [branch] if (_UdonLightVolumeProbesBlend) { // No Volume B found and light volumes blending enabled
            LV_SampleLightProbe(L0_B, L1r_B, L1g_B, L1b_B);
        } else {
            LV_SampleVolume(volumesCount - 1, localUVW, L0_B, L1r_B, L1g_B, L1b_B);
        }
    } else {
        LV_SampleVolume(volumeID_B, localUVW_B, L0_B, L1r_B, L1g_B, L1b_B);
    }

    // Lerping SH components
    L0  += lerp(L0_B,  L0_A,  mask);
    L1r += lerp(L1r_B, L1r_A, mask);
    L1g += lerp(L1g_B, L1g_A, mask);
    L1b += lerp(L1b_B, L1b_A, mask);

}

// Calculates L1 SH based on the world position from additive volumes only.
void LV_LightVolumeAdditiveSH(float3 worldPos, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b) {
    uint additiveCount = min((uint) _UdonLightVolumeAdditiveCount, VRCLV_MAX_VOLUMES_COUNT); // Clamping global iteration counts
    [branch] if (additiveCount == 0) return;

    uint maxOverdraw = min((uint) _UdonLightVolumeAdditiveMaxOverdraw, additiveCount);
    [branch] if (maxOverdraw == 0) return;

    uint addVolumesCount = 0;
    [loop] for (uint id = 0; id < additiveCount && addVolumesCount < maxOverdraw; id++) {
        float3 localUVW = LV_LocalFromVolume(id, worldPos);
        [branch] if (LV_PointLocalAABB(localUVW)) {
            LV_SampleVolume(id, localUVW, L0, L1r, L1g, L1b);
            addVolumesCount++;
        }
    }
}

// ----------------------- VRC LIGHT VOLUMES PUBLIC API --------------------------

// Checks if Light Volumes are used in this scene. Returns 0 if not, returns 1 if enabled
float LightVolumesEnabled() {
    return (_UdonLightVolumeEnabled != 0 && _UdonLightVolumeVersion >= VRCLV_MIN_SUPPORTED_VERSION) ? 1 : 0;
}

// Returns the light volumes version
float LightVolumesVersion() {
    float version = _UdonLightVolumeVersion;
    return version == 0 ? _UdonLightVolumeEnabled : version;
}

// Calculates L1 SH based on the world position. Samples regular volumes, additive volumes and Point Light Volumes.
void LightVolumeSH(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, float3 worldPosOffset = 0, float3 worldNormal = 0, float pointLightShading = 1) {
    L0 = 0; L1r = 0; L1g = 0; L1b = 0;
    [branch] if (_UdonLightVolumeEnabled == 0 || _UdonLightVolumeVersion < VRCLV_MIN_SUPPORTED_VERSION) {
        LV_SampleLightProbe(L0, L1r, L1g, L1b);
    } else {
        LV_LightVolumeRegularSH(worldPos + worldPosOffset, L0, L1r, L1g, L1b);
        LV_LightVolumeAdditiveSH(worldPos + worldPosOffset, L0, L1r, L1g, L1b);
        LV_PointLightVolumeSH(worldPos, worldNormal, pointLightShading, L0, L1r, L1g, L1b);
    }
}

// Calculates L0 SH based on the world position. Samples regular volumes, additive volumes and Point Light Volumes.
float3 LightVolumeSH_L0(float3 worldPos, float3 worldPosOffset = 0, float3 worldNormal = 0, float pointLightShading = 1) {
    [branch] if (_UdonLightVolumeEnabled == 0 || _UdonLightVolumeVersion < VRCLV_MIN_SUPPORTED_VERSION) {
        return float3(unity_SHAr.w, unity_SHAg.w, unity_SHAb.w);
    } else {
        float3 L0 = 0, unused_L1 = 0; // Let's just pray that compiler will strip everything x.x
        LV_LightVolumeRegularSH(worldPos + worldPosOffset, L0, unused_L1, unused_L1, unused_L1);
        LV_LightVolumeAdditiveSH(worldPos + worldPosOffset, L0, unused_L1, unused_L1, unused_L1);
        LV_PointLightVolumeSH(worldPos, worldNormal, pointLightShading, L0, unused_L1, unused_L1, unused_L1);
        return L0;
    }
}

// Calculates L1 SH based on the world position from Additive Light Volumes and Point Light Volumes.
void LightVolumeAdditiveSH(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, float3 worldPosOffset = 0, float3 worldNormal = 0, float pointLightShading = 1) {
    L0 = 0; L1r = 0; L1g = 0; L1b = 0;
    [branch] if (_UdonLightVolumeEnabled != 0 && _UdonLightVolumeVersion >= VRCLV_MIN_SUPPORTED_VERSION) {
        LV_LightVolumeAdditiveSH(worldPos + worldPosOffset, L0, L1r, L1g, L1b);
        LV_PointLightVolumeSH(worldPos, worldNormal, pointLightShading, L0, L1r, L1g, L1b);
    }
}

// Calculates L0 SH based on the world position from additive volumes and Point Light Volumes.
float3 LightVolumeAdditiveSH_L0(float3 worldPos, float3 worldPosOffset = 0, float3 worldNormal = 0, float pointLightShading = 1) {
    [branch] if (_UdonLightVolumeEnabled == 0 || _UdonLightVolumeVersion < VRCLV_MIN_SUPPORTED_VERSION) {
        return 0;
    } else {
        float3 L0 = 0, unused_L1 = 0; // Let's just pray that compiler will strip everything x.x
        LV_LightVolumeAdditiveSH(worldPos + worldPosOffset, L0, unused_L1, unused_L1, unused_L1);
        LV_PointLightVolumeSH(worldPos, worldNormal, pointLightShading, L0, unused_L1, unused_L1, unused_L1);
        return L0;
    }
}

// Calculate Light Volume Color based on all SH components provided and the world normal
float3 LightVolumeEvaluate(float3 worldNormal, float3 L0, float3 L1r, float3 L1g, float3 L1b) {
    return L0 + float3(dot(L1r, worldNormal), dot(L1g, worldNormal), dot(L1b, worldNormal));
}

// Calculates speculars for light volumes or any SH L1 data with provided f0
float3 LightVolumeSpecular(float3 f0, float smoothness, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b) {
    return LV_LightVolumeSpecular(f0, smoothness, worldNormal, viewDir, L0, L1r, L1g, L1b);
}

// Calculates speculars for light volumes or any SH L1 data
float3 LightVolumeSpecular(float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b) {
    return LightVolumeSpecular(lerp(0.04, albedo, metallic), smoothness, worldNormal, viewDir, L0, L1r, L1g, L1b);
}

// Calculates speculars for light volumes or any SH L1 data, but simplified, with only one dominant direction with provided f0
float3 LightVolumeSpecularDominant(float3 f0, float smoothness, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b) {
    return LV_Specular(f0, smoothness, worldNormal, viewDir, L0, L1r + L1g + L1b);
}

// Calculates speculars for light volumes or any SH L1 data, but simplified, with only one dominant direction
float3 LightVolumeSpecularDominant(float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b) {
    return LightVolumeSpecularDominant(lerp(0.04, albedo, metallic), smoothness, worldNormal, viewDir, L0, L1r, L1g, L1b);
}

// Calculates L1 SH and speculars based on custom f0. Volumes use dominant SH specular, Point Light Volumes are accumulated individually.
void LightVolumeSHSpecular(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, out float3 specular, float3 f0, float smoothness, float3 worldNormal, float3 viewDir, float3 worldPosOffset = 0, float pointLightShading = 1) {
    L0 = 0; L1r = 0; L1g = 0; L1b = 0; specular = 0;
    [branch] if (_UdonLightVolumeEnabled == 0 || _UdonLightVolumeVersion < VRCLV_MIN_SUPPORTED_VERSION) {
        LV_SampleLightProbe(L0, L1r, L1g, L1b);
        specular = LV_Specular(f0, smoothness, worldNormal, viewDir, L0, L1r + L1g + L1b);
    } else {
        LV_LightVolumeRegularSH(worldPos + worldPosOffset, L0, L1r, L1g, L1b);
        LV_LightVolumeAdditiveSH(worldPos + worldPosOffset, L0, L1r, L1g, L1b);
        specular = LV_Specular(f0, smoothness, worldNormal, viewDir, L0, L1r + L1g + L1b);
        LV_PointLightVolumeSHSpecular(worldPos, worldNormal, viewDir, smoothness, f0, pointLightShading, L0, L1r, L1g, L1b, specular);
    }
}

// Calculates L1 SH and speculars based on PBR data. Volumes use dominant SH specular, Point Light Volumes are accumulated individually.
void LightVolumeSHSpecular(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, out float3 specular, float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir, float3 worldPosOffset = 0, float pointLightShading = 1) {
    LightVolumeSHSpecular(worldPos, L0, L1r, L1g, L1b, specular, lerp(0.04, albedo, metallic), smoothness, worldNormal, viewDir, worldPosOffset, pointLightShading);
}

// Calculates additive L1 SH and speculars based on custom f0. Additive volumes use dominant SH specular, Point Light Volumes are accumulated individually.
void LightVolumeAdditiveSHSpecular(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, out float3 specular, float3 f0, float smoothness, float3 worldNormal, float3 viewDir, float3 worldPosOffset = 0, float pointLightShading = 1) {
    L0 = 0; L1r = 0; L1g = 0; L1b = 0; specular = 0;
    [branch] if (_UdonLightVolumeEnabled != 0 && _UdonLightVolumeVersion >= VRCLV_MIN_SUPPORTED_VERSION) {
        LV_LightVolumeAdditiveSH(worldPos + worldPosOffset, L0, L1r, L1g, L1b);
        specular = LV_Specular(f0, smoothness, worldNormal, viewDir, L0, L1r + L1g + L1b);
        LV_PointLightVolumeSHSpecular(worldPos, worldNormal, viewDir, smoothness, f0, pointLightShading, L0, L1r, L1g, L1b, specular);
    }
}

// Calculates additive L1 SH and speculars based on PBR data. Additive volumes use dominant SH specular, Point Light Volumes are accumulated individually.
void LightVolumeAdditiveSHSpecular(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, out float3 specular, float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir, float3 worldPosOffset = 0, float pointLightShading = 1) {
    LightVolumeAdditiveSHSpecular(worldPos, L0, L1r, L1g, L1b, specular, lerp(0.04, albedo, metallic), smoothness, worldNormal, viewDir, worldPosOffset, pointLightShading);
}

#endif
